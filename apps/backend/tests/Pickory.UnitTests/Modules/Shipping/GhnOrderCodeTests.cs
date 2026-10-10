using Pickory.Modules.Shipping.Shipments;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping;

// SHIP-01: mã `client_order_code` gửi GHN theo số lần đã hủy.
public sealed class GhnOrderCodeTests
{
    [Fact]
    // Lần tạo đầu dùng đúng `ref`, để bấm tạo lại sau lỗi mạng gửi cùng mã và GHN trả vận đơn cũ.
    public void For_NoCancellation_ReturnsReference()
    {
        Assert.Equal("ORD-1", GhnOrderCode.For("ORD-1", 0));
    }

    [Theory]
    [InlineData(1, "ORD-1-2")]
    [InlineData(2, "ORD-1-3")]
    [InlineData(9, "ORD-1-10")]
    // Sau n lần hủy hậu tố là n+1, nên lần tạo lại đầu tiên mang hậu tố 2 như plan.
    public void For_AfterCancellations_AppendsCountPlusOne(int cancelledCount, string expected)
    {
        Assert.Equal(expected, GhnOrderCode.For("ORD-1", cancelledCount));
    }

    [Fact]
    public void For_ReferenceOfExactlyFiftyCharacters_IsAccepted()
    {
        var reference = new string('a', 50);

        Assert.Equal(reference, GhnOrderCode.For(reference, 0));
    }

    [Fact]
    public void For_ReferenceOfFiftyOneCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => GhnOrderCode.For(new string('a', 51), 0));
    }

    [Fact]
    // `ref` 48 ký tự cộng hậu tố `-2` thành đúng 50 ký tự nên được nhận.
    public void For_SuffixReachesExactlyFifty_IsAccepted()
    {
        var code = GhnOrderCode.For(new string('a', 48), 1);

        Assert.Equal(50, code.Length);
        Assert.EndsWith("-2", code);
    }

    [Fact]
    // `ref` 49 ký tự hợp lệ khi đứng một mình, nhưng hậu tố `-2` đẩy mã lên 51 ký tự nên bị từ chối.
    public void For_SuffixPushesFromFortyNineToFiftyOne_Throws()
    {
        Assert.Equal(49, GhnOrderCode.For(new string('a', 49), 0).Length);
        Assert.Throws<ArgumentException>(() => GhnOrderCode.For(new string('a', 49), 1));
    }

    [Fact]
    // Hậu tố hai chữ số dài hơn hậu tố một chữ số. Biên phải tính theo độ dài thật của hậu tố.
    public void For_TwoDigitSuffixIsMeasuredByRealLength()
    {
        Assert.Equal(50, GhnOrderCode.For(new string('a', 47), 9).Length);
        Assert.Throws<ArgumentException>(() => GhnOrderCode.For(new string('a', 48), 9));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void For_BlankReference_Throws(string? reference)
    {
        Assert.Throws<ArgumentException>(() => GhnOrderCode.For(reference!, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void For_NegativeCancelledCount_Throws(int cancelledCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GhnOrderCode.For("ORD-1", cancelledCount));
    }

    [Fact]
    // Số lần hủy lớn nhất vẫn cho hậu tố dương `2147483648`. Cộng 1 bằng `int` sẽ tràn thành số âm.
    public void For_CancelledCountAtIntMaximum_DoesNotOverflowSuffix()
    {
        Assert.Equal("ORD-1-2147483648", GhnOrderCode.For("ORD-1", int.MaxValue));
    }
}
