using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Shipping.Contracts.Quotes;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Checkout;

// COM-27: nguồn phí của shipping đổi đúng sang nguồn phí ghi trên đơn, và nhãn trong response viết thường.
public sealed class ShippingFeeSourceMapperTests
{
    [Theory]
    [InlineData(QuoteSource.Ghn, ShippingFeeSource.Ghn, "ghn")]
    [InlineData(QuoteSource.Cached, ShippingFeeSource.Cached, "cached")]
    [InlineData(QuoteSource.Fallback, ShippingFeeSource.Fallback, "fallback")]
    public void FromQuote_MapsEachSourceAndLabelMatchesDbEnumLabel(
        QuoteSource quoteSource, ShippingFeeSource expected, string expectedLabel)
    {
        var mapped = ShippingFeeSourceMapper.FromQuote(quoteSource);

        Assert.Equal(expected, mapped);
        Assert.Equal(expectedLabel, OrderLabels.ToLabel(mapped));
    }

    [Fact]
    public void FromQuote_UnknownSource_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShippingFeeSourceMapper.FromQuote((QuoteSource)99));
    }

    [Fact]
    public void EveryQuoteSourceHasAMatchingOrderSource()
    {
        // Thêm giá trị mới vào `QuoteSource` mà quên ánh xạ làm test này đỏ, thay vì lỗi lúc đặt đơn.
        foreach (var source in Enum.GetValues<QuoteSource>())
        {
            var mapped = ShippingFeeSourceMapper.FromQuote(source);
            Assert.Equal(source.ToString(), mapped.ToString());
        }
    }
}
