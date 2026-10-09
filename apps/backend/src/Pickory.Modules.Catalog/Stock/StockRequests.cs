using Pickory.Modules.Catalog.Contracts.Stock;

namespace Pickory.Modules.Catalog.Stock;

// Chuẩn hóa danh sách SKU của đơn trước khi giữ hàng.
public static class StockRequests
{
    // Gộp mục trùng SKU bằng cách cộng số lượng, rồi sắp theo `skuId`. Mọi đơn khóa SKU theo cùng một thứ tự.
    public static IReadOnlyList<StockItem> MergeItems(IReadOnlyList<StockItem> items)
    {
        foreach (var item in items)
        {
            // Số lượng không dương là lỗi lập trình của bên gọi. Từ chối từng mục để +2 và -2 không triệt tiêu nhau.
            if (string.IsNullOrWhiteSpace(item.SkuId))
            {
                throw new ArgumentException("Mục giữ hàng phải có `skuId`.", nameof(items));
            }

            if (item.Quantity <= 0)
            {
                throw new ArgumentException($"Số lượng của SKU {item.SkuId} phải lớn hơn 0.", nameof(items));
            }
        }

        return items
            .GroupBy(item => item.SkuId, StringComparer.Ordinal)
            .Select(group => new StockItem(group.Key, group.Sum(item => item.Quantity)))
            .OrderBy(item => item.SkuId, StringComparer.Ordinal)
            .ToList();
    }
}
