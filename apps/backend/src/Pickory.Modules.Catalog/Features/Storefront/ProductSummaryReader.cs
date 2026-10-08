using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Reading;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Tóm tắt kèm danh mục lá của sản phẩm. Trang so sánh cần danh mục để kiểm các sản phẩm cùng loại.
public sealed record ProductSummaryRow(ProductSummary Summary, string CategoryId);

// Đọc tóm tắt sản phẩm theo danh sách id cho khách. `GetProductsAsync` và trang so sánh dùng chung.
public sealed class ProductSummaryReader(CatalogDbContext database, CloudinaryCredentials credentials)
{
    public async Task<IReadOnlyList<ProductSummaryRow>> ReadByIdsAsync(
        IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        // Bỏ id rỗng và id trùng trước khi truy vấn. Mỗi sản phẩm chỉ xuất hiện một lần trong kết quả.
        var requestedIds = RequestedIdOrder.Normalize(ids);
        if (requestedIds.Count == 0)
        {
            return [];
        }

        // Lọc qua `VisibleToCustomers` để `draft` và `hidden` bị bỏ như id không tồn tại.
        var rows = await database.Products.AsNoTracking()
            .VisibleToCustomers()
            .Where(product => requestedIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Slug,
                product.Name,
                product.CategoryId,
                BrandId = product.Brand!.Id,
                BrandName = product.Brand.Name,
                BrandSlug = product.Brand.Slug,
                product.MinPrice,
                product.InStock,
                product.Status,
                // Ảnh vị trí đầu là ảnh đại diện. Thêm `Id` làm khóa phụ phòng hai ảnh cùng vị trí.
                Thumbnail = product.Images
                    .OrderBy(image => image.Position).ThenBy(image => image.Id)
                    .Select(image => new { image.PublicId, image.Alt, image.SkuId })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var summaries = rows.Select(row => new ProductSummaryRow(
            new ProductSummary(
                row.Id,
                row.Slug,
                row.Name,
                new BrandRef(row.BrandId, row.BrandName, row.BrandSlug),
                row.MinPrice,
                row.InStock,
                ProductDetailAssembler.StatusName(row.Status),
                row.Thumbnail is null
                    ? null
                    : new ProductImageInfo(ProductImageUrl.Build(credentials, row.Thumbnail.PublicId), row.Thumbnail.Alt, row.Thumbnail.SkuId)),
            row.CategoryId));

        // DB không giữ thứ tự của `ids`. Sắp lại để người gọi nhận đúng thứ tự đã yêu cầu.
        return RequestedIdOrder.Arrange(requestedIds, summaries, row => row.Summary.Id);
    }
}
