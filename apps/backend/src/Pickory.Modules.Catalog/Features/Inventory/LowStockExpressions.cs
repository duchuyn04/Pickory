using System.Linq.Expressions;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;

namespace Pickory.Modules.Catalog.Features.Inventory;

// Định nghĩa duy nhất của "sắp hết hàng". Danh sách của trang quản trị và `CountLowStockAsync` cùng dùng, nên hai số luôn khớp.
public static class LowStockExpressions
{
    // SKU tắt không bán nên không cần nhập thêm. SKU bật có tồn kho không vượt ngưỡng thì sắp hết hàng.
    public static readonly Expression<Func<Sku, bool>> IsLowStock =
        sku => sku.Enabled && sku.Stock <= CatalogInventoryDefaults.LowStockThreshold;

    public static IQueryable<Sku> WhereLowStock(this IQueryable<Sku> skus) => skus.Where(IsLowStock);
}
