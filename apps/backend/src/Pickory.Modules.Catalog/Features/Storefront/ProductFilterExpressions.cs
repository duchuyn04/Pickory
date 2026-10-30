using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Các điều kiện lọc dưới dạng biểu thức để EF Core dịch sang SQL. Danh sách và facets dùng chung, nên hai bên không lệch nhau.
public static class ProductFilterExpressions
{
    // Khách chỉ thấy sản phẩm chưa xóa mềm và đang bán hoặc ngừng kinh doanh. `draft` và `hidden` không bao giờ lộ ra.
    public static IQueryable<Product> VisibleToCustomers(this IQueryable<Product> products) =>
        products.Where(product => product.DeletedAt == null
            && (product.Status == ProductStatus.Active || product.Status == ProductStatus.Discontinued));

    public static IQueryable<Product> InCategories(this IQueryable<Product> products, IReadOnlyCollection<string> categoryIds) =>
        products.Where(product => categoryIds.Contains(product.CategoryId));

    public static IQueryable<Product> WithBrands(this IQueryable<Product> products, IReadOnlyCollection<string> brandSlugs) =>
        products.Where(product => brandSlugs.Contains(product.Brand!.Slug));

    public static IQueryable<Product> WithPriceBetween(this IQueryable<Product> products, int? priceMin, int? priceMax)
    {
        if (priceMin is not null)
        {
            products = products.Where(product => product.MinPrice >= priceMin);
        }

        if (priceMax is not null)
        {
            products = products.Where(product => product.MinPrice <= priceMax);
        }

        return products;
    }

    public static IQueryable<Product> OnlyInStock(this IQueryable<Product> products) =>
        products.Where(product => product.InStock);

    // Áp một bộ lọc thông số. Giá trị lấy thẳng từ cột `specs` kiểu `jsonb` bằng toán tử JSON của PostgreSQL.
    public static IQueryable<Product> WithAttribute(this IQueryable<Product> products, AttributeFilter filter)
    {
        var key = filter.Key;
        switch (filter)
        {
            case ValueListFilter valueList:
                var values = valueList.Values.ToArray();
                return products.Where(product => values.Contains(product.Specs.RootElement.GetProperty(key).GetString()!));

            case BooleanFilter boolean:
                var expected = boolean.Value;
                return products.Where(product => product.Specs.RootElement.GetProperty(key).GetBoolean() == expected);

            case NumberRangeFilter range:
                if (range.Min is not null)
                {
                    var minimum = range.Min.Value;
                    products = products.Where(product => product.Specs.RootElement.GetProperty(key).GetDecimal() >= minimum);
                }

                if (range.Max is not null)
                {
                    var maximum = range.Max.Value;
                    products = products.Where(product => product.Specs.RootElement.GetProperty(key).GetDecimal() <= maximum);
                }

                return products;

            default:
                throw new ArgumentOutOfRangeException(nameof(filter), filter, "Loại bộ lọc thông số chưa được hỗ trợ.");
        }
    }

    // Mọi kiểu sắp xếp thêm `Id` làm khóa phụ. Nhờ đó các sản phẩm hòa nhau luôn theo một thứ tự, và hai trang liên tiếp không trùng hay sót.
    public static IQueryable<Product> OrderedBy(this IQueryable<Product> products, ProductSort sort) => sort switch
    {
        ProductSort.PriceAscending => products.OrderBy(product => product.MinPrice).ThenBy(product => product.Id),
        ProductSort.PriceDescending => products.OrderByDescending(product => product.MinPrice).ThenBy(product => product.Id),
        _ => products.OrderByDescending(product => product.CreatedAt).ThenBy(product => product.Id),
    };
}
