using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Catalog.Features.Storefront;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Persistence;

namespace Pickory.Modules.Catalog.Reading;

public sealed class CatalogReader(
    CatalogDbContext database,
    ProductDetailService detailService,
    ProductSummaryReader summaryReader,
    ProductSearchService searchService,
    CloudinaryCredentials credentials) : ICatalogReader
{
    private const int MinListLimit = 1;

    public Task<ProductDetail?> GetProductAsync(string idOrSlug, CancellationToken cancellationToken) =>
        detailService.FindByIdOrSlugAsync(idOrSlug, cancellationToken);

    // Dựng URL ảnh bằng hàm chung của catalog để module khác không cần tự ghép chuỗi URL.
    public string BuildProductImageUrl(string publicId) =>
        ProductImageUrl.Build(credentials, publicId);

    public async Task<IReadOnlyList<ProductSummary>> GetProductsAsync(
        IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var rows = await summaryReader.ReadByIdsAsync(ids, cancellationToken);
        return rows.Select(row => row.Summary).ToList();
    }

    public async Task<IReadOnlyList<ProductCategoryInfo>> GetProductCategoriesAsync(
        IReadOnlyList<string> productIds, CancellationToken cancellationToken)
    {
        var requestedIds = RequestedIdOrder.Normalize(productIds);
        if (requestedIds.Count == 0)
        {
            return [];
        }

        // Lọc cùng điều kiện với `GetProductsAsync`. Báo cáo không tách nhóm theo sản phẩm khách không thấy.
        var rows = await database.Products.AsNoTracking()
            .VisibleToCustomers()
            .Where(product => requestedIds.Contains(product.Id))
            .Select(product => new ProductCategoryInfo(
                product.Id,
                new CategoryRef(product.Category!.Id, product.Category.Name, product.Category.Slug)))
            .ToListAsync(cancellationToken);

        // DB không giữ thứ tự của `productIds`. Sắp lại để người gọi nhận đúng thứ tự đã yêu cầu.
        return RequestedIdOrder.Arrange(requestedIds, rows, row => row.ProductId);
    }

    public async Task<IReadOnlyList<SkuSnapshot>> GetSkusAsync(
        IReadOnlyList<string> skuIds, CancellationToken cancellationToken)
    {
        var requestedIds = RequestedIdOrder.Normalize(skuIds);
        if (requestedIds.Count == 0)
        {
            return [];
        }

        // Không lọc theo trạng thái ở đây. Giỏ hàng cần biết SKU tắt hoặc sản phẩm ngừng bán để giải thích vì sao không mua được.
        // Giữ SKU của sản phẩm đã xóa mềm với cờ `ProductDeleted`. Giỏ hàng cần phân biệt hàng đã xóa với hàng tắt hoặc ngừng bán.
        var rows = await database.Skus.AsNoTracking()
            .Where(sku => requestedIds.Contains(sku.Id))
            .Select(sku => new
            {
                sku.Id,
                sku.Code,
                sku.ProductId,
                ProductName = sku.Product!.Name,
                ProductSlug = sku.Product.Slug,
                ProductStatus = sku.Product.Status,
                ProductDeleted = sku.Product.DeletedAt != null,
                sku.Enabled,
                sku.OptionValues,
                sku.Price,
                sku.CompareAtPrice,
                sku.Stock,
                sku.WeightGrams,
                sku.LengthCm,
                sku.WidthCm,
                sku.HeightCm,
            })
            .ToListAsync(cancellationToken);

        var productIds = rows.Select(row => row.ProductId).Distinct().ToList();
        var images = await database.ProductImages.AsNoTracking()
            .Where(image => productIds.Contains(image.ProductId))
            .OrderBy(image => image.Position).ThenBy(image => image.Id)
            .Select(image => new { image.ProductId, image.PublicId, image.Alt, image.SkuId })
            .ToListAsync(cancellationToken);
        var imagesByProduct = images.ToLookup(image => image.ProductId);

        var snapshots = rows.Select(row =>
        {
            // Ảnh riêng của SKU thắng. Không có thì dùng ảnh đầu của sản phẩm để giỏ hàng luôn có ảnh nếu sản phẩm có ảnh.
            var candidates = imagesByProduct[row.ProductId]
                .Select(image => new ImageCandidate(image.PublicId, image.Alt, image.SkuId))
                .ToList();
            var chosen = SkuImageChooser.Choose(row.Id, candidates);

            return new SkuSnapshot(
                row.Id,
                row.Code,
                row.ProductId,
                row.ProductName,
                row.ProductSlug,
                ProductDetailAssembler.StatusName(row.ProductStatus),
                row.Enabled,
                ProductDetailAssembler.ReadOptionValues(row.OptionValues),
                row.Price,
                row.CompareAtPrice,
                row.Stock,
                row.WeightGrams,
                row.LengthCm,
                row.WidthCm,
                row.HeightCm,
                chosen is null
                    ? null
                    : new ProductImageInfo(ProductImageUrl.Build(credentials, chosen.PublicId), chosen.Alt, chosen.SkuId),
                chosen?.PublicId,
                row.ProductDeleted);
        });

        // DB không giữ thứ tự của `skuIds`. Sắp lại để giỏ hàng nhận đúng thứ tự đã gửi.
        return RequestedIdOrder.Arrange(requestedIds, snapshots, snapshot => snapshot.SkuId);
    }

    public Task<IndexingPage> ListForIndexingAsync(string? cursor, int pageSize, CancellationToken cancellationToken) =>
        detailService.ListForIndexingAsync(cursor, pageSize, cancellationToken);

    public async Task<IReadOnlyList<AttributeDefinitionInfo>> GetAttributeDefinitionsAsync(
        string categoryId, CancellationToken cancellationToken)
    {
        var categories = await CategoryAttributeIndex.LoadAsync(database, cancellationToken);
        return categories.EffectiveDefinitions(categoryId);
    }

    public async Task<IReadOnlyList<ProductSummary>> ListProductsAsync(
        ProductFilters filters, int limit, CancellationToken cancellationToken)
    {
        // Kéo `limit` về khoảng của route công khai. Module khác không nạp được nhiều hơn một trang lớn nhất.
        var pageSize = Math.Clamp(limit, MinListLimit, ProductSearchQuery.MaxPageSize);

        // Đi qua cùng bộ đọc tham số với `GET /api/products`, nên giá trị sai bị từ chối giống nhau.
        var query = ProductSearchQuery.Read(ToParameters(filters, pageSize));
        var result = await searchService.SearchAsync(query, cancellationToken);

        return result.Items.Select(item => new ProductSummary(
            item.Id,
            item.Slug,
            item.Name,
            new BrandRef(item.Brand.Id, item.Brand.Name, item.Brand.Slug),
            item.MinPrice,
            item.InStock,
            item.Status,
            item.Image is null ? null : new ProductImageInfo(item.Image.Url, item.Image.Alt, null))).ToList();
    }

    public Task<int> CountLowStockAsync(CancellationToken cancellationToken) =>
        // Sản phẩm đã xóa không cần nhập thêm hàng, nên bỏ SKU của chúng khỏi cảnh báo tồn kho thấp.
        database.Skus.AsNoTracking()
            .Where(sku => sku.Product!.DeletedAt == null)
            .WhereLowStock()
            .CountAsync(cancellationToken);

    private static Dictionary<string, string> ToParameters(ProductFilters filters, int pageSize)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pageSize"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        AddIfPresent(parameters, "category", filters.CategorySlug);
        if (filters.BrandSlugs is { Count: > 0 })
        {
            parameters["brand"] = string.Join(',', filters.BrandSlugs);
        }

        AddIfPresent(parameters, "priceMin", filters.PriceMin?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddIfPresent(parameters, "priceMax", filters.PriceMax?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (filters.InStockOnly)
        {
            parameters["inStock"] = "true";
        }

        AddIfPresent(parameters, "sort", filters.Sort);
        foreach (var (key, value) in filters.Attributes ?? new Dictionary<string, string>())
        {
            parameters[AttributeFilterReader.ParameterPrefix + key] = value;
        }

        return parameters;
    }

    private static void AddIfPresent(Dictionary<string, string> parameters, string name, string? value)
    {
        if (value is not null)
        {
            parameters[name] = value;
        }
    }
}
