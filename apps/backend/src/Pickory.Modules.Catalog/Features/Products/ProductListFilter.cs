using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;
using Pickory.Shared.Http;

namespace Pickory.Modules.Catalog.Features.Products;

// Câu truy vấn lọc sản phẩm viết một lần. Route danh sách và route xuất file cùng gọi, nên hai route luôn thấy cùng tập sản phẩm.
public static class ProductListFilter
{
    // Chỉ áp các điều kiện lọc. Hàm không đọc `filter.Page`, nên route xuất bỏ qua phân trang mà không cần nhánh riêng.
    public static IQueryable<Product> Apply(IQueryable<Product> products, ProductListQuery filter)
    {
        if (filter.Search is { } term)
        {
            // Tên so sánh không dấu và không phân biệt hoa thường. Từ khóa đi qua `unaccent` như cột, để hai phía cùng dạng.
            var pattern = term.LikePattern;
            products = products.Where(product =>
                EF.Functions.ILike(EF.Functions.Unaccent(product.Name), EF.Functions.Unaccent(pattern), SearchTerm.EscapeCharacter)
                // Mã biến thể (SKU) không có dấu, nên không cần `unaccent`. Mẫu đã escape `%` và `_` trong từ khóa.
                || product.Skus.Any(sku => EF.Functions.ILike(sku.Code, pattern, SearchTerm.EscapeCharacter)));
        }

        // Lọc danh mục bao gồm cả các sản phẩm thuộc danh mục con trực thuộc.
        var categoryId = filter.CategoryId?.Trim();
        if (!string.IsNullOrEmpty(categoryId))
        {
            products = products.Where(product => product.CategoryId == categoryId
                || (product.Category != null && product.Category.ParentId == categoryId));
        }

        // Lọc trạng thái chính xác theo giá trị hợp lệ được truyền từ tham số.
        if (filter.Status.HasValue)
        {
            products = products.Where(product => product.Status == filter.Status.Value);
        }

        // Lọc sản phẩm có ít nhất một biến thể đang bật và tồn kho dưới hoặc bằng ngưỡng.
        if (filter.LowStock)
        {
            products = products.Where(product => product.Skus.AsQueryable().Any(LowStockExpressions.IsLowStock));
        }

        return products;
    }

    // Mới tạo lên đầu, rồi theo `Id`. Thiếu `Id` thì phân trang lặp hoặc sót các sản phẩm cùng thời điểm tạo.
    public static IOrderedQueryable<Product> NewestFirst(IQueryable<Product> products)
    {
        return products
            .OrderByDescending(product => product.CreatedAt)
            .ThenBy(product => product.Id);
    }
}
