using System.Linq.Expressions;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Products;

// Định nghĩa duy nhất của "sắp hết hàng". Danh sách sản phẩm và `CountLowStockAsync` cùng dùng, nên hai số luôn khớp.
public static class LowStockExpressions
{
    // SKU tắt không bán nên không cần nhập thêm. SKU bật có tồn kho không vượt ngưỡng thì sắp hết hàng.
    public static readonly Expression<Func<Sku, bool>> IsLowStock =
        sku => sku.Enabled && sku.Stock <= CatalogInventoryDefaults.LowStockThreshold;

    public static IQueryable<Sku> WhereLowStock(this IQueryable<Sku> skus) => skus.Where(IsLowStock);
}
