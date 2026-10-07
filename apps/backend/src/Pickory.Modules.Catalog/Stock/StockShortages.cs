using Pickory.Modules.Catalog.Contracts.Stock;

namespace Pickory.Modules.Catalog.Stock;

// Thay đổi tồn kho mong muốn của một SKU. `Delta` âm là lấy hàng ra.
public sealed record StockDelta(string SkuId, int Delta);

public static class StockShortages
{
    // Liệt kê SKU không đủ hàng theo thứ tự `skuId`. SKU vắng trong `available` coi như còn 0 cái.
    public static IReadOnlyList<StockShortage> Find(
        IReadOnlyList<StockDelta> deltas, IReadOnlyDictionary<string, int> available)
    {
        var shortages = new List<StockShortage>();
        foreach (var change in deltas.OrderBy(change => change.SkuId, StringComparer.Ordinal))
        {
            var remaining = available.GetValueOrDefault(change.SkuId, 0);
            // Chỉ lấy hàng ra mới có thể thiếu. Nhập hàng (delta dương) luôn hợp lệ.
            if (change.Delta < 0 && remaining + change.Delta < 0)
            {
                shortages.Add(new StockShortage(change.SkuId, -change.Delta, remaining));
            }
        }

        return shortages;
    }
}
