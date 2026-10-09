using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Catalog.Descriptions;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Features.Brands;
using Pickory.Modules.Catalog.Features.Categories;
using Pickory.Modules.Catalog.Features.Products;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Catalog.Stock;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Audit;

namespace Pickory.Modules.Catalog.Seeding;

// Số bản ghi mỗi loại mà một lần chạy tạo mới hoặc bỏ qua. Lần chạy thứ hai phải toàn là bỏ qua.
public sealed class SeedReport
{
    private readonly Dictionary<string, (int Created, int Skipped)> _counts = new(StringComparer.Ordinal);

    public void Created(string kind, int amount = 1) => Add(kind, amount, 0);

    public void Skipped(string kind, int amount = 1) => Add(kind, 0, amount);

    public int CreatedCount(string kind) => _counts.TryGetValue(kind, out var pair) ? pair.Created : 0;

    public int SkippedCount(string kind) => _counts.TryGetValue(kind, out var pair) ? pair.Skipped : 0;

    public IEnumerable<string> Lines() =>
        _counts.Select(pair => $"{pair.Key}: tạo mới {pair.Value.Created}, bỏ qua {pair.Value.Skipped}");

    private void Add(string kind, int created, int skipped)
    {
        _counts.TryGetValue(kind, out var current);
        _counts[kind] = (current.Created + created, current.Skipped + skipped);
    }
}

// Lỗi của dữ liệu mẫu, ví dụ sản phẩm không qua điều kiện đăng bán. Lệnh in lý do và thoát với mã lỗi.
public sealed class SeedException(string message) : Exception(message);

// Ghi dữ liệu mẫu qua cùng handler của trang quản trị. Nhờ vậy luật slug, SKU, giá và đăng bán không bị viết lại.
public static class CatalogSeeder
{
    public const string StockReason = "Dữ liệu mẫu";
    public const string ImageFolder = "pickory/products/seed";

    // Dữ liệu mẫu không do một người cụ thể thao tác, nên không có actor thật để ghi nhật ký quản trị.
    private static readonly Actor SystemActor = new()
    {
        AnonymousId = "catalog-seed",
        UserId = "catalog-seed",
        Kind = ActorKind.Staff,
        Roles = [],
        Permissions = [],
        Consent = ConsentState.AllDisabled,
    };

    public static async Task<SeedReport> SeedAsync(
        IServiceProvider services, SeedCatalog catalog, CancellationToken cancellationToken)
    {
        var report = new SeedReport();
        var categoryIds = await SeedCategoriesAsync(services, catalog, report, cancellationToken);
        var brandIds = await SeedBrandsAsync(services, catalog, report, cancellationToken);
        var packagingByCategory = catalog.Categories.ToDictionary(category => category.Name, category => category.Packaging);

        foreach (var product in catalog.Products)
        {
            // Mỗi sản phẩm một scope và một DbContext, để bộ nhớ không phình khi seed hàng trăm sản phẩm.
            await using var scope = services.CreateAsyncScope();
            await SeedProductAsync(
                scope.ServiceProvider, product, categoryIds[product.CategoryName], brandIds[product.BrandName],
                packagingByCategory[product.CategoryName], report, cancellationToken);
        }

        return report;
    }

    private static async Task<Dictionary<string, string>> SeedCategoriesAsync(
        IServiceProvider services, SeedCatalog catalog, SeedReport report, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var handler = CreateHandler<CategoryManagementHandler>(scope.ServiceProvider);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in catalog.Categories)
        {
            // Khóa tự nhiên là slug sinh từ tên. Danh mục đã có thì bỏ qua và không ghi đè thuộc tính.
            var slug = SlugGenerator.ToSlug(category.Name);
            var existingId = await database.Categories.AsNoTracking()
                .Where(item => item.Slug == slug).Select(item => item.Id).FirstOrDefaultAsync(cancellationToken);
            if (existingId is not null)
            {
                ids[category.Name] = existingId;
                report.Skipped("Danh mục");
                continue;
            }

            var created = await handler.CreateAsync(
                SystemActor, new CreateCategoryRequest(category.Name, null, 0, category.Attributes), cancellationToken);
            ids[category.Name] = created.Id;
            report.Created("Danh mục");
        }

        return ids;
    }

    private static async Task<Dictionary<string, string>> SeedBrandsAsync(
        IServiceProvider services, SeedCatalog catalog, SeedReport report, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var handler = CreateHandler<BrandManagementHandler>(scope.ServiceProvider);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var brandName in catalog.BrandNames)
        {
            var slug = SlugGenerator.ToSlug(brandName);
            var existingId = await database.Brands.AsNoTracking()
                .Where(item => item.Slug == slug).Select(item => item.Id).FirstOrDefaultAsync(cancellationToken);
            if (existingId is not null)
            {
                ids[brandName] = existingId;
                report.Skipped("Thương hiệu");
                continue;
            }

            var created = await handler.CreateAsync(SystemActor, new CreateBrandRequest(brandName, null), cancellationToken);
            ids[brandName] = created.Id;
            report.Created("Thương hiệu");
        }

        return ids;
    }

    private static async Task SeedProductAsync(
        IServiceProvider provider, SeedProduct seed, string categoryId, string brandId, SeedPackaging packaging,
        SeedReport report, CancellationToken cancellationToken)
    {
        var database = provider.GetRequiredService<CatalogDbContext>();
        var handler = CreateHandler<ProductManagementHandler>(provider);

        // Khóa tự nhiên là slug. Sản phẩm đã có thì chỉ bỏ bước tạo, còn tồn kho và trạng thái vẫn được kiểm để lệnh chạy dở chạy lại được.
        var productId = await database.Products.AsNoTracking()
            .Where(item => item.Slug == seed.Slug).Select(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        if (productId is null)
        {
            var created = await handler.CreateAsync(
                SystemActor, BuildRequest(seed, categoryId, brandId, packaging), cancellationToken);
            productId = created.Id;
            report.Created("Sản phẩm");
            report.Created("SKU", created.Variants.Count);
            report.Created("Ảnh", created.Images.Count);
        }
        else
        {
            report.Skipped("Sản phẩm");
        }

        await SeedStockAsync(provider, database, seed, productId, report, cancellationToken);
        await SeedStatusAsync(database, handler, seed, productId, report, cancellationToken);
    }

    private static CreateProductRequest BuildRequest(
        SeedProduct seed, string categoryId, string brandId, SeedPackaging packaging)
    {
        // `FromPlainText` kiểm tài liệu bằng `DescriptionSchema.Validate` trước khi trả về. Lỗi được đổi thành lỗi dữ liệu mẫu để lệnh dừng và nêu tên sản phẩm.
        JsonObject description;
        try
        {
            description = DescriptionText.FromPlainText(seed.Description);
        }
        catch (InvalidOperationException exception)
        {
            throw new SeedException($"Mô tả của \"{seed.Name}\" không hợp lệ: {exception.Message}");
        }

        var images = Enumerable.Range(1, seed.ImageCount)
            .Select(number => new ProductImageRequest($"{ImageFolder}/{seed.Slug}-{number}", $"{seed.Name} - ảnh {number}"))
            .ToList();
        var variants = seed.Variants.Select(variant => new ProductVariantRequest(
            variant.OptionValues, Code: null, variant.Price, variant.CompareAtPrice, Enabled: true,
            packaging.WeightGrams, packaging.LengthCm, packaging.WidthCm, packaging.HeightCm)).ToList();

        return new CreateProductRequest(
            categoryId, brandId, seed.Name, seed.ShortDesc,
            JsonSerializer.SerializeToElement(description),
            JsonSerializer.SerializeToElement(seed.Specs),
            seed.Options, seed.WarrantyMonths, variants, images);
    }

    // Ghi tồn kho ban đầu bằng movement `manual`. Cột `stock` không bao giờ được gán trực tiếp.
    private static async Task SeedStockAsync(
        IServiceProvider provider, CatalogDbContext database, SeedProduct seed, string productId, SeedReport report,
        CancellationToken cancellationToken)
    {
        var skus = await database.Skus.AsNoTracking().Where(sku => sku.ProductId == productId).ToListAsync(cancellationToken);
        var movements = new List<StockMovementInput>();
        foreach (var variant in seed.Variants.Where(variant => variant.Stock > 0))
        {
            var sku = skus.FirstOrDefault(item => SameOptions(item.OptionValues, variant.OptionValues))
                ?? throw new SeedException($"Không tìm thấy SKU của \"{seed.Name}\" cho tùy chọn {string.Join(", ", variant.OptionValues)}.");
            // Đã có movement dữ liệu mẫu cho SKU này thì bỏ qua, để lần chạy thứ hai không cộng tồn kho thêm.
            var alreadySeeded = await database.InventoryMovements.AnyAsync(movement =>
                movement.SkuId == sku.Id && movement.Type == InventoryMovementType.Manual && movement.Reason == StockReason,
                cancellationToken);
            if (alreadySeeded)
            {
                report.Skipped("Movement tồn kho");
                continue;
            }

            movements.Add(new StockMovementInput(sku.Id, variant.Stock, InventoryMovementType.Manual, null, null, StockReason));
        }

        if (movements.Count == 0)
        {
            return;
        }

        var timeProvider = provider.GetRequiredService<TimeProvider>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var result = await StockLedger.ApplyAsync(
            transaction.GetDbTransaction(), movements, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (!result.Applied)
        {
            throw new SeedException($"Không ghi được tồn kho của \"{seed.Name}\".");
        }

        await transaction.CommitAsync(cancellationToken);
        report.Created("Movement tồn kho", movements.Count);
    }

    // Đăng bán qua `ChangeStatusAsync`, nên `ProductPublishing.CheckPublishable` chạy trước mỗi lần đổi sang `active`.
    private static async Task SeedStatusAsync(
        CatalogDbContext database, ProductManagementHandler handler, SeedProduct seed, string productId,
        SeedReport report, CancellationToken cancellationToken)
    {
        var current = await database.Products.AsNoTracking()
            .Where(item => item.Id == productId).Select(item => item.Status).SingleAsync(cancellationToken);
        if (seed.Status == ProductStatus.Draft || current == seed.Status)
        {
            return;
        }

        try
        {
            // Sản phẩm ngừng kinh doanh phải qua `active` trước, vì bản nháp không chuyển thẳng sang `discontinued`.
            if (current == ProductStatus.Draft)
            {
                await handler.ChangeStatusAsync(SystemActor, productId, ProductStatus.Active, cancellationToken);
                report.Created("Chuyển trạng thái");
            }

            if (seed.Status == ProductStatus.Discontinued)
            {
                await handler.ChangeStatusAsync(SystemActor, productId, ProductStatus.Discontinued, cancellationToken);
                report.Created("Chuyển trạng thái");
            }
        }
        catch (ProductNotPublishableException exception)
        {
            // Sản phẩm mẫu không qua điều kiện đăng bán là lỗi dữ liệu mẫu. In đủ lý do để sửa và dừng lệnh.
            throw new SeedException(
                $"Sản phẩm \"{seed.Name}\" chưa đủ điều kiện đăng bán: {string.Join(" | ", exception.Reasons)}");
        }
    }

    private static bool SameOptions(JsonDocument stored, IReadOnlyDictionary<string, string> expected)
    {
        var values = stored.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString());
        return values.Count == expected.Count && expected.All(pair => values.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    // Handler nhận `IAuditLogWriter` rỗng, vì nhật ký quản trị cần người dùng thật còn dữ liệu mẫu thì không có.
    private static THandler CreateHandler<THandler>(IServiceProvider provider) where THandler : class =>
        ActivatorUtilities.CreateInstance<THandler>(provider, NullAuditLogWriter.Instance);
}

// Bỏ qua mọi nhật ký quản trị của lệnh seed.
internal sealed class NullAuditLogWriter : IAuditLogWriter
{
    public static readonly NullAuditLogWriter Instance = new();

    public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;
}
