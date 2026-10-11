using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Catalog.Caching;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Catalog.Features.Brands;

// Quản lý thông tin thương hiệu phục vụ phân loại và hiển thị sản phẩm.
public sealed class BrandManagementHandler(
    CatalogDbContext database,
    IAuditLogWriter auditLogWriter,
    TimeProvider timeProvider,
    CatalogResponseCache responseCache)
{
    // Đọc danh sách thương hiệu sắp xếp theo tên để phục vụ bộ lọc công khai.
    public async Task<IReadOnlyList<BrandResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var brands = await database.Brands
            .AsNoTracking()
            .OrderBy(brand => brand.Name)
            .ToListAsync(cancellationToken);

        return brands.Select(ToResponse).ToList();
    }

    // Tạo mới một thương hiệu và ghi nhật ký kiểm toán quản trị.
    public async Task<BrandResponse> CreateAsync(
        Actor actor,
        CreateBrandRequest request,
        CancellationToken cancellationToken)
    {
        var slug = await GenerateUniqueSlugAsync(request.Name, excludeId: null, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.Name.Trim(),
            Slug = slug,
            LogoPublicId = request.LogoPublicId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Brands.Add(brand);
        await database.SaveChangesAsync(cancellationToken);

        // Ghi nhật ký tạo thương hiệu trong cùng giao dịch để đảm bảo tính toàn vẹn.
        var auditEntry = new AuditLogEntry(
            actor.UserId!,
            "catalog.brand.create",
            "brand",
            brand.Id,
            null,
            ToAuditSnapshot(brand));
        await auditLogWriter.WriteAsync(auditEntry, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Xóa cache sau commit. Request song song nạp lại từ DB sẽ thấy dữ liệu mới.
        responseCache.Invalidate();

        return ToResponse(brand);
    }

    // Cập nhật thông tin thương hiệu nhưng giữ nguyên slug để không làm hỏng URL.
    public async Task<BrandResponse> UpdateAsync(
        Actor actor,
        string id,
        UpdateBrandRequest request,
        CancellationToken cancellationToken)
    {
        var brand = await database.Brands.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);
        if (brand is null)
        {
            // Báo lỗi không tìm thấy khi mã thương hiệu không tồn tại trong cơ sở dữ liệu.
            throw new AppException("BRAND_NOT_FOUND", 404, "Không tìm thấy thương hiệu.");
        }

        var beforeSnapshot = ToAuditSnapshot(brand);

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != brand.Name)
        {
            brand.Name = request.Name.Trim();
            // Giữ nguyên slug ban đầu để tránh làm hỏng các liên kết chia sẻ đã có.
        }

        if (request.LogoPublicId is not null)
        {
            brand.LogoPublicId = request.LogoPublicId;
        }

        brand.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        // Ghi nhật ký cập nhật thương hiệu trong cùng giao dịch cơ sở dữ liệu.
        var auditEntry = new AuditLogEntry(
            actor.UserId!,
            "catalog.brand.update",
            "brand",
            brand.Id,
            beforeSnapshot,
            ToAuditSnapshot(brand));
        await auditLogWriter.WriteAsync(auditEntry, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Xóa cache sau commit. Request song song nạp lại từ DB sẽ thấy dữ liệu mới.
        responseCache.Invalidate();

        return ToResponse(brand);
    }

    // Xóa thương hiệu khỏi hệ thống khi thương hiệu không còn gắn với sản phẩm nào.
    public async Task DeleteAsync(Actor actor, string id, CancellationToken cancellationToken)
    {
        var brand = await database.Brands.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);
        if (brand is null)
        {
            // Báo lỗi không tìm thấy khi mã thương hiệu cần xóa không tồn tại trong hệ thống.
            throw new AppException("BRAND_NOT_FOUND", 404, "Không tìm thấy thương hiệu.");
        }

        // Chặn xóa nếu còn sản phẩm của thương hiệu này để tránh dữ liệu mồ côi.
        var hasProducts = await database.Products.AnyAsync(
            product => product.BrandId == id,
            cancellationToken);
        if (hasProducts)
        {
            throw new AppException("BRAND_HAS_PRODUCTS", 400, "Không thể xóa thương hiệu đang có sản phẩm.");
        }

        var beforeSnapshot = ToAuditSnapshot(brand);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Brands.Remove(brand);
        await database.SaveChangesAsync(cancellationToken);

        // Ghi nhật ký thao tác xóa thương hiệu phục vụ kiểm toán sau này.
        var auditEntry = new AuditLogEntry(
            actor.UserId!,
            "catalog.brand.delete",
            "brand",
            brand.Id,
            beforeSnapshot,
            null);
        await auditLogWriter.WriteAsync(auditEntry, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Xóa cache sau commit. Request song song nạp lại từ DB sẽ thấy dữ liệu mới.
        responseCache.Invalidate();
    }

    private async Task<string> GenerateUniqueSlugAsync(string name, string? excludeId, CancellationToken cancellationToken)
    {
        var baseSlug = SlugGenerator.ToSlug(name);
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            // Dùng tên thay thế khi chuỗi chỉ gồm ký tự đặc biệt tạo ra slug rỗng.
            baseSlug = "thuong-hieu";
        }

        var slug = baseSlug;
        var counter = 2;
        // Lặp kiểm tra để đảm bảo slug không bị trùng với thương hiệu khác.
        while (await database.Brands.AnyAsync(
            item => item.Slug == slug && item.Id != excludeId,
            cancellationToken))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        return slug;
    }

    private static BrandResponse ToResponse(Brand brand)
    {
        return new BrandResponse(
            brand.Id,
            brand.Name,
            brand.Slug,
            brand.LogoPublicId,
            brand.CreatedAt,
            brand.UpdatedAt);
    }

    private static object ToAuditSnapshot(Brand brand)
    {
        return new
        {
            brand.Id,
            brand.Name,
            brand.Slug,
            brand.LogoPublicId,
            brand.CreatedAt,
            brand.UpdatedAt,
        };
    }
}
