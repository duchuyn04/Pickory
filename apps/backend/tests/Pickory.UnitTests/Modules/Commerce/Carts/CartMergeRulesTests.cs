using Pickory.Modules.Commerce.Carts;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Carts;

public sealed class CartMergeRulesTests
{
    private const int MaxQuantityPerSku = 5;
    private const int MaxCartLines = 3;

    private static IReadOnlyDictionary<string, int> Merge(
        CartLineInput[] account, CartLineInput[] guest, int maxLines = MaxCartLines) =>
        CartMergeRules.Merge(account, guest, MaxQuantityPerSku, maxLines)
            .ToDictionary(line => line.SkuId, line => line.Quantity);

    [Fact]
    // Cùng SKU thì cộng số lượng khi tổng chưa vượt giới hạn.
    public void Merge_SameSkuBelowLimit_SumsQuantity()
    {
        var merged = Merge([new("a", 2)], [new("a", 2)]);

        Assert.Equal(4, merged["a"]);
    }

    [Fact]
    // Tổng vượt giới hạn thì kẹp ở giới hạn. Giá trị biên: tổng đúng bằng giới hạn giữ nguyên.
    public void Merge_SameSkuAboveLimit_ClampsAtMaximum()
    {
        var merged = Merge([new("a", 3), new("b", 2)], [new("a", 4), new("b", 3)]);

        Assert.Equal(5, merged["a"]);
        Assert.Equal(5, merged["b"]);
    }

    [Fact]
    // Dòng tài khoản đã vượt giới hạn (do giới hạn bị hạ sau này) không bị gộp làm giảm số lượng.
    public void Merge_AccountLineAlreadyAboveLimit_IsNotLowered()
    {
        var merged = Merge([new("a", 8)], [new("a", 1)]);

        Assert.Equal(8, merged["a"]);
    }

    [Fact]
    // Dòng mới của giỏ vãng lai cũng bị kẹp số lượng theo giới hạn mỗi SKU.
    public void Merge_NewGuestLineAboveLimit_IsClamped()
    {
        var merged = Merge([], [new("a", 9)]);

        Assert.Equal(5, merged["a"]);
    }

    [Fact]
    // Giỏ đã đủ số dòng thì dòng mới bị bỏ theo thứ tự `skuId`, dòng cộng vào SKU có sẵn vẫn được giữ.
    public void Merge_LineLimitReached_DropsNewLinesInSkuOrder_ButSumsExistingLines()
    {
        var merged = Merge(
            [new("b", 1)],
            [new("e", 1), new("d", 1), new("b", 1), new("c", 1)],
            maxLines: 3);

        Assert.Equal(["b", "c", "d"], merged.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, merged["b"]);
    }

    [Fact]
    // Kết quả không phụ thuộc thứ tự dòng đầu vào, để hai lần gộp cho cùng kết quả.
    public void Merge_ResultDoesNotDependOnGuestLineOrder()
    {
        CartLineInput[] forward = [new("a", 1), new("b", 1), new("c", 1), new("d", 1)];
        CartLineInput[] reversed = [.. forward.Reverse()];

        var first = Merge([], forward, maxLines: 2);
        var second = Merge([], reversed, maxLines: 2);

        Assert.Equal(first.OrderBy(pair => pair.Key), second.OrderBy(pair => pair.Key));
        Assert.Equal(["a", "b"], first.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Merge_EmptyGuest_KeepsAccountLinesUnchanged()
    {
        var merged = Merge([new("a", 2), new("b", 9)], []);

        Assert.Equal(2, merged["a"]);
        Assert.Equal(9, merged["b"]);
    }
}
