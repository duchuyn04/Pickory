using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Chạy truy vấn danh sách sản phẩm công khai và tính facets. Module khác gọi lại được cùng bộ lọc qua `SearchAsync`.
public sealed class ProductSearchService(CatalogDbContext database, CloudinaryCredentials cloudinary)
{
    public async Task<ProductListResponse> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken)
    {
        var scope = await ResolveCategoryScopeAsync(query.CategorySlug, cancellationToken);

        // Chỉ thuộc tính `filterable` của danh mục được lọc. Khóa ngoài tập này trả `UNKNOWN_FILTER`.
        var attributeFilters = AttributeFilterReader.Read(query.AttributeParameters, scope.FilterableAttributes);

        var filters = new ActiveFilters(query, attributeFilters);
        var matching = ApplyFilters(scope.Products, filters, excludedAttributeKey: null, excludeBrand: false);

        // Đếm và lấy trang chạy trong DB. Không nạp mọi sản phẩm vào bộ nhớ rồi mới lọc.
        var total = await matching.CountAsync(cancellationToken);
        var items = await LoadPageAsync(matching, query, cancellationToken);
        var facets = await BuildFacetsAsync(scope, filters, cancellationToken);

        return new ProductListResponse(items, query.Page, query.PageSize, total, facets);
    }

    // Mỗi nhóm facets bỏ đúng bộ lọc của chính nó, theo Decision 12, để người dùng vẫn thấy số đếm của các giá trị khác.
    private static IQueryable<Product> ApplyFilters(
        IQueryable<Product> products, ActiveFilters filters, string? excludedAttributeKey, bool excludeBrand)
    {
        if (!excludeBrand && filters.Query.BrandSlugs.Count > 0)
        {
            products = products.WithBrands(filters.Query.BrandSlugs);
        }

        products = products.WithPriceBetween(filters.Query.PriceMin, filters.Query.PriceMax);

        if (filters.Query.InStockOnly)
        {
            products = products.OnlyInStock();
        }

        foreach (var attributeFilter in filters.AttributeFilters)
        {
            if (attributeFilter.Key != excludedAttributeKey)
            {
                products = products.WithAttribute(attributeFilter);
            }
        }

        return products;
    }

    private async Task<IReadOnlyList<ProductListItem>> LoadPageAsync(
        IQueryable<Product> matching, ProductSearchQuery query, CancellationToken cancellationToken)
    {
        var rows = await matching
            .OrderedBy(query.Sort)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(product => new
            {
                product.Id,
                product.Slug,
                product.Name,
                BrandId = product.Brand!.Id,
                BrandName = product.Brand.Name,
                BrandSlug = product.Brand.Slug,
                product.MinPrice,
                product.InStock,
                product.Status,
                // Ảnh vị trí đầu là ảnh đại diện. Thêm `Id` làm khóa phụ phòng hai ảnh cùng vị trí.
                Thumbnail = product.Images
                    .OrderBy(image => image.Position).ThenBy(image => image.Id)
                    .Select(image => new { image.PublicId, image.Alt })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new ProductListItem(
                row.Id,
                row.Slug,
                row.Name,
                new ProductBrandSummary(row.BrandId, row.BrandName, row.BrandSlug),
                row.MinPrice,
                row.InStock,
                row.Status.ToString().ToLowerInvariant(),
                row.Thumbnail is null
                    ? null
                    : new ProductThumbnail(
                        row.Thumbnail.PublicId, row.Thumbnail.Alt, ProductImageUrl.Build(cloudinary, row.Thumbnail.PublicId))))
            .ToList();
    }

    private async Task<ProductFacets> BuildFacetsAsync(
        CategoryScope scope, ActiveFilters filters, CancellationToken cancellationToken)
    {
        var brands = await BuildBrandFacetsAsync(scope, filters, cancellationToken);

        var attributes = new List<AttributeFacet>(scope.FilterableAttributes.Count);
        foreach (var definition in scope.FilterableAttributes)
        {
            attributes.Add(await BuildAttributeFacetAsync(scope, filters, definition, cancellationToken));
        }

        return new ProductFacets(brands, attributes);
    }

    private async Task<IReadOnlyList<BrandFacet>> BuildBrandFacetsAsync(
        CategoryScope scope, ActiveFilters filters, CancellationToken cancellationToken)
    {
        // Facets hãng áp mọi bộ lọc khác nhưng bỏ bộ lọc `brand`.
        var products = ApplyFilters(scope.Products, filters, excludedAttributeKey: null, excludeBrand: true);

        // Nhóm trong DB. Hãng không còn sản phẩm nào không xuất hiện nên không có số đếm 0.
        var rows = await products
            .GroupBy(product => new { product.Brand!.Slug, product.Brand.Name })
            .Select(group => new { group.Key.Slug, group.Key.Name, Count = group.Count() })
            .OrderBy(row => row.Name).ThenBy(row => row.Slug)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new BrandFacet(row.Slug, row.Name, row.Count)).ToList();
    }

    private static async Task<AttributeFacet> BuildAttributeFacetAsync(
        CategoryScope scope, ActiveFilters filters, AttributeDefinition definition, CancellationToken cancellationToken)
    {
        // Facets của thuộc tính bỏ đúng bộ lọc của thuộc tính đó và áp mọi bộ lọc còn lại.
        var products = ApplyFilters(scope.Products, filters, excludedAttributeKey: definition.Key, excludeBrand: false);
        var key = definition.Key;

        if (definition.Type == AttributeTypes.Number)
        {
            var range = await products
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Min = group.Min(product => (decimal?)product.Specs.RootElement.GetProperty(key).GetDecimal()),
                    Max = group.Max(product => (decimal?)product.Specs.RootElement.GetProperty(key).GetDecimal()),
                })
                .FirstOrDefaultAsync(cancellationToken);
            return new AttributeFacet(definition.Key, definition.Label, definition.Type, definition.Unit, null, range?.Min, range?.Max);
        }

        if (definition.Type == AttributeTypes.Boolean)
        {
            var counts = await products
                // `->>` của PostgreSQL trả chữ cho mọi kiểu JSON, kể cả boolean. Điều kiện bỏ sản phẩm thiếu khóa này, nên facets không có nhóm `null`.
                .Where(product => product.Specs.RootElement.GetProperty(key).GetString() != null)
                .GroupBy(product => product.Specs.RootElement.GetProperty(key).GetBoolean())
                .Select(group => new { Value = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            // Thứ tự `false` rồi `true` cố định để giao diện không nhảy hàng giữa các lần lọc.
            var booleanValues = counts
                .OrderBy(row => row.Value)
                .Select(row => new AttributeFacetValue(row.Value, row.Count))
                .ToList();
            return new AttributeFacet(definition.Key, definition.Label, definition.Type, definition.Unit, booleanValues, null, null);
        }

        var textCounts = await products
            // `->>` của PostgreSQL trả chữ cho mọi kiểu JSON, kể cả boolean. Điều kiện bỏ sản phẩm thiếu khóa này, nên facets không có nhóm `null`.
            .Where(product => product.Specs.RootElement.GetProperty(key).GetString() != null)
            .GroupBy(product => product.Specs.RootElement.GetProperty(key).GetString()!)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // `enum` theo thứ tự lựa chọn của danh mục. `text` theo bảng chữ cái để thứ tự ổn định.
        var optionOrder = definition.Options ?? [];
        var ordered = definition.Type == AttributeTypes.Enum
            ? textCounts.OrderBy(row => OptionIndex(optionOrder, row.Value)).ThenBy(row => row.Value, StringComparer.Ordinal)
            : textCounts.OrderBy(row => row.Value, StringComparer.Ordinal);
        var values = ordered.Select(row => new AttributeFacetValue(row.Value, row.Count)).ToList();
        return new AttributeFacet(definition.Key, definition.Label, definition.Type, definition.Unit, values, null, null);
    }

    private static int OptionIndex(IReadOnlyList<string> options, string value)
    {
        for (var index = 0; index < options.Count; index++)
        {
            if (options[index] == value)
            {
                return index;
            }
        }

        // Giá trị cũ không còn trong danh sách lựa chọn đứng cuối.
        return int.MaxValue;
    }

    // Tìm danh mục theo slug, lấy mọi danh mục con và gộp thuộc tính kế thừa từ danh mục cha.
    private async Task<CategoryScope> ResolveCategoryScopeAsync(string? categorySlug, CancellationToken cancellationToken)
    {
        if (categorySlug is null)
        {
            // Không có `category` thì không có thuộc tính nào lọc được.
            return new CategoryScope([], database.Products.AsNoTracking().VisibleToCustomers());
        }

        // Bảng danh mục nhỏ (cây tối đa 2 cấp). Nạp một lần để tìm cha và con mà không cần truy vấn đệ quy.
        var categories = await database.Categories
            .AsNoTracking()
            .Select(category => new { category.Id, category.ParentId, category.Slug, category.Attributes })
            .ToListAsync(cancellationToken);

        var selected = categories.FirstOrDefault(category => category.Slug == categorySlug);
        if (selected is null)
        {
            throw new AppException("CATEGORY_NOT_FOUND", 404, "Không tìm thấy danh mục.");
        }

        var byId = categories.ToDictionary(category => category.Id);
        var parentById = categories.ToDictionary(category => category.Id, category => category.ParentId);

        IReadOnlyList<AttributeDefinition> effective = [];
        foreach (var categoryId in CategoryTreeWalker.AncestorChain(selected.Id, parentById))
        {
            var own = DeserializeAttributes(byId[categoryId].Attributes);
            effective = CategoryAttributes.Resolve(effective, own);
        }

        var categoryIds = CategoryTreeWalker.SelfAndDescendants(selected.Id, parentById);
        var filterable = effective.Where(attribute => attribute.Filterable).ToList();

        return new CategoryScope(
            filterable,
            database.Products.AsNoTracking().VisibleToCustomers().InCategories(categoryIds));
    }

    private static IReadOnlyList<AttributeDefinition> DeserializeAttributes(JsonDocument document) =>
        JsonSerializer.Deserialize<List<AttributeDefinition>>(document.RootElement.GetRawText(), AttributeDefinition.JsonOptions) ?? [];

    // Phạm vi danh mục của một lần tìm kiếm. `Products` là sản phẩm khách thấy được trong phạm vi đó.
    private sealed record CategoryScope(
        IReadOnlyList<AttributeDefinition> FilterableAttributes,
        IQueryable<Product> Products);

    private sealed record ActiveFilters(ProductSearchQuery Query, IReadOnlyList<AttributeFilter> AttributeFilters);
}
