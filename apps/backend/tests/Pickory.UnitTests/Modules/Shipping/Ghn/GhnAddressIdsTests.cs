using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-10 lát 3: mã tỉnh và mã phường lưu dạng chuỗi, nhưng GHN chỉ nhận số ở các trường `*_id_v2`.
public sealed class GhnAddressIdsTests
{
    [Fact]
    public void TryParse_NumericCodes_ReturnsNumbers()
    {
        var parsed = GhnAddressIds.TryParse("1000001", "1003646", out var provinceId, out var wardId);

        Assert.True(parsed);
        Assert.Equal(1000001L, provinceId);
        Assert.Equal(1003646L, wardId);
    }

    [Theory]
    [InlineData("abc", "1003646")]
    [InlineData("1000001", "abc")]
    [InlineData("1000001", "")]
    [InlineData("", "1003646")]
    [InlineData("1000001", " 1003646")]
    [InlineData("1000001", "-5")]
    [InlineData("1000001", "+1003646")]
    [InlineData("1000001", "0")]
    [InlineData("1000001", "10036.46")]
    [InlineData("1000001", "99999999999999999999")]
    // Mã không đọc được thành số dương thì không gửi GHN, vì GHN sẽ trả lỗi hệ thống khó hiểu.
    public void TryParse_CodeIsNotPositiveNumber_ReturnsFalse(string provinceCode, string wardCode)
    {
        Assert.False(GhnAddressIds.TryParse(provinceCode, wardCode, out _, out _));
    }
}
