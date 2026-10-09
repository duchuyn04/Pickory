using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Catalog.Stock;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Stock;

// CAT-08: gộp mục giữ hàng trùng SKU và dựng danh sách thiếu hàng.
public sealed class StockRequestsTests
{
    [Fact]
    // Hai mục cùng SKU cộng số lượng. Kết quả sắp theo `skuId` để mọi đơn khóa SKU theo cùng một thứ tự.
    public void MergeItems_SumsDuplicateSkusAndSortsById()
    {
        var merged = StockRequests.MergeItems([new("b", 2), new("a", 1), new("b", 3)]);

        Assert.Equal([new StockItem("a", 1), new StockItem("b", 5)], merged);
    }

    [Fact]
    public void MergeItems_EmptyList_ReturnsEmpty()
    {
        Assert.Empty(StockRequests.MergeItems([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    // Số lượng không dương là lỗi của bên gọi. Hàm ném lỗi thay vì âm thầm bỏ mục.
    public void MergeItems_NonPositiveQuantity_Throws(int quantity)
    {
        Assert.Throws<ArgumentException>(() => StockRequests.MergeItems([new("a", quantity)]));
    }

    [Fact]
    // Hai mục +2 và -2 cùng SKU sẽ tự triệt tiêu nếu gộp trước khi kiểm. Số lượng từng mục phải dương.
    public void MergeItems_RejectsEvenWhenOtherItemIsPositive()
    {
        Assert.Throws<ArgumentException>(() => StockRequests.MergeItems([new("a", 2), new("a", -2)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void MergeItems_BlankSkuId_Throws(string skuId)
    {
        Assert.Throws<ArgumentException>(() => StockRequests.MergeItems([new(skuId, 1)]));
    }

    [Fact]
    // Chỉ SKU thiếu hàng xuất hiện trong danh sách, theo thứ tự `skuId`.
    public void FindShortages_ListsOnlyShortSkusInSkuOrder()
    {
        var shortages = StockShortages.Find(
            [new("c", -4), new("a", -1), new("b", -3)],
            new Dictionary<string, int> { ["a"] = 5, ["b"] = 2, ["c"] = 1 });

        Assert.Equal([new StockShortage("b", 3, 2), new StockShortage("c", 4, 1)], shortages);
    }

    [Fact]
    // Đủ hàng đúng bằng số cần giữ thì không thiếu.
    public void FindShortages_ExactStock_IsNotShort()
    {
        Assert.Empty(StockShortages.Find([new("a", -5)], new Dictionary<string, int> { ["a"] = 5 }));
    }

    [Fact]
    // SKU không có trong kết quả đọc coi như còn 0 cái, nên đơn nhắc tới SKU lạ bị báo thiếu.
    public void FindShortages_UnknownSku_HasZeroAvailable()
    {
        var shortages = StockShortages.Find([new("ghost", -2)], new Dictionary<string, int>());

        Assert.Equal([new StockShortage("ghost", 2, 0)], shortages);
    }

    [Fact]
    // Cộng hàng vào kho (delta dương) không bao giờ gây thiếu.
    public void FindShortages_PositiveDelta_IsNeverShort()
    {
        Assert.Empty(StockShortages.Find([new("a", 10)], new Dictionary<string, int> { ["a"] = 0 }));
    }
}
