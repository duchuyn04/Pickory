using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Phản hồi của `GET /api/products/compare`. `Category` là `null` khi không còn sản phẩm nào khách thấy được.
public sealed record ProductComparisonResponse(
    CategoryRef? Category,
    IReadOnlyList<AttributeGroup> AttributeGroups,
    IReadOnlyList<ComparedProduct> Products);

// `Specs` có đủ khóa của mọi thuộc tính hiệu lực. Giá trị là `null` khi sản phẩm chưa nhập thuộc tính đó.
public sealed record ComparedProduct(ProductSummary Summary, IReadOnlyDictionary<string, JsonElement?> Specs);

public sealed class ProductComparisonService(CatalogDbContext database, ProductSummaryReader summaryReader)
{
    public async Task<ProductComparisonResponse> CompareAsync(
        IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        // Id lạ hoặc sản phẩm bị ẩn bị bỏ qua. Liên kết so sánh cũ nhờ đó vẫn mở được khi một sản phẩm ngừng hiển thị.
        var rows = await summaryReader.ReadByIdsAsync(ids, cancellationToken);
        if (rows.Count == 0)
        {
            return new ProductComparisonResponse(null, [], []);
        }

        // Bảng so sánh chỉ có nghĩa khi mọi sản phẩm dùng chung một bộ thuộc tính, tức cùng danh mục lá.
        var categoryId = rows[0].CategoryId;
        if (rows.Any(row => row.CategoryId != categoryId))
        {
            throw new AppException(
                "COMPARE_CATEGORY_MISMATCH", 400, "Chỉ so sánh được các sản phẩm cùng danh mục.");
        }

        var categories = await CategoryAttributeIndex.LoadAsync(database, cancellationToken);
        var definitions = categories.EffectiveDefinitions(categoryId);

        var productIds = rows.Select(row => row.Summary.Id).ToList();
        var specsById = await database.Products.AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new { product.Id, product.Specs })
            .ToDictionaryAsync(product => product.Id, product => product.Specs, cancellationToken);

        var products = rows.Select(row => new ComparedProduct(
            row.Summary,
            // Mỗi thuộc tính hiệu lực có một khóa, kể cả thuộc tính không `filterable`. Bảng so sánh nhờ đó đủ dòng.
            definitions.ToDictionary(
                definition => definition.Key,
                definition => ProductDetailAssembler.ReadSpecValue(specsById[row.Summary.Id], definition.Key)))).ToList();

        return new ProductComparisonResponse(
            categories.FindCategory(categoryId), AttributeGrouping.Group(definitions), products);
    }
}
