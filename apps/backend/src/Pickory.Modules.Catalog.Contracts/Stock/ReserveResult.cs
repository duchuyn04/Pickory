namespace Pickory.Modules.Catalog.Contracts.Stock;

// Kết quả giữ hàng. Khi `Ok` là false, `Shortages` liệt kê mọi SKU thiếu và catalog không giữ SKU nào.
public sealed record ReserveResult(bool Ok, IReadOnlyList<StockShortage> Shortages)
{
    public static ReserveResult Success { get; } = new(true, []);

    public static ReserveResult Short(IReadOnlyList<StockShortage> shortages) => new(false, shortages);
}
