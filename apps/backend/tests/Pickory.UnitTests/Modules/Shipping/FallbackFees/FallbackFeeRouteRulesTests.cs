using Pickory.Modules.Shipping.FallbackFees;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.FallbackFees;

// SHIP-13: xác định tuyến từ mã tỉnh lấy hàng và mã tỉnh nhận.
public sealed class FallbackFeeRouteRulesTests
{
    private const string HaNoi = "1000000";
    private const string HaiPhong = "1000004";
    private const string Hue = "1000002";
    private const string DaNang = "1000003";
    private const string HoChiMinh = "1000001";
    private const string UnknownProvince = "1000099";

    [Theory]
    [InlineData(HaNoi, HaNoi)]
    [InlineData(HoChiMinh, HoChiMinh)]
    // Cùng mã thì nội tỉnh, kể cả khi mã không có trong bảng vùng.
    [InlineData(UnknownProvince, UnknownProvince)]
    public void Determine_SameProvinceCode_IsSameProvince(string from, string to)
    {
        Assert.Equal(FallbackFeeRoute.SameProvince, FallbackFeeRouteRules.Determine(from, to));
    }

    [Theory]
    [InlineData(HaNoi, HaiPhong)]
    [InlineData(Hue, DaNang)]
    public void Determine_DifferentProvinceSameRegion_IsSameRegion(string from, string to)
    {
        Assert.Equal(FallbackFeeRoute.SameRegion, FallbackFeeRouteRules.Determine(from, to));
    }

    [Theory]
    [InlineData(HaNoi, HoChiMinh)]
    [InlineData(HoChiMinh, Hue)]
    [InlineData(DaNang, HaiPhong)]
    public void Determine_DifferentRegion_IsCrossRegion(string from, string to)
    {
        Assert.Equal(FallbackFeeRoute.CrossRegion, FallbackFeeRouteRules.Determine(from, to));
    }

    [Theory]
    [InlineData(UnknownProvince, HaNoi)]
    [InlineData(HaNoi, UnknownProvince)]
    [InlineData(UnknownProvince, "1000098")]
    [InlineData("", HaNoi)]
    public void Determine_ProvinceOutsideTable_IsUnknown(string from, string to)
    {
        Assert.Equal(FallbackFeeRoute.Unknown, FallbackFeeRouteRules.Determine(from, to));
    }
}
