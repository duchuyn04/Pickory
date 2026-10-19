using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.FallbackFees;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.FallbackFees;

// SHIP-14: chọn có tính được phí dự phòng hay không, rồi tính theo tuyến. Logic thuần, không đọc DB.
public sealed class FallbackFeeQuoterTests
{
    private const string HaNoi = "1000000";
    private const string HaiPhong = "1000004";
    private const string HoChiMinh = "1000001";
    private const string UnknownProvince = "1000099";
    private const string AnotherUnknownProvince = "1000098";

    private static readonly Parcel ThousandGrams = new(1000, 10, 10, 10);

    // Bảng đủ chín giá trị: nội tỉnh 20.000 đ, nội vùng 30.000 đ, liên vùng 40.000 đ (khối lượng gốc 2000 g).
    private static ShippingFallbackFee FullConfig() => new()
    {
        Id = "singleton",
        Enabled = true,
        SameProvinceBaseFee = 20_000,
        SameProvinceBaseWeightGrams = 1000,
        SameProvinceStepFee = 5_000,
        SameRegionBaseFee = 30_000,
        SameRegionBaseWeightGrams = 1000,
        SameRegionStepFee = 6_000,
        CrossRegionBaseFee = 40_000,
        CrossRegionBaseWeightGrams = 2000,
        CrossRegionStepFee = 7_000,
    };

    [Theory]
    [InlineData(HaNoi, HaNoi, "SameProvince", 20_000)]
    [InlineData(HaNoi, HaiPhong, "SameRegion", 30_000)]
    [InlineData(HaNoi, HoChiMinh, "CrossRegion", 40_000)]
    // Hai mã ngoài bảng vùng vẫn là nội tỉnh khi giống nhau, vì luật tuyến không cần biết vùng.
    [InlineData(UnknownProvince, UnknownProvince, "SameProvince", 20_000)]
    public void TryQuote_EnabledWithFullTable_UsesTierOfTheRoute(
        string pickupProvince, string receiverProvince, string expectedRouteName, int expectedFee)
    {
        var quote = FallbackFeeQuoter.TryQuote(FullConfig(), pickupProvince, receiverProvince, ThousandGrams);

        Assert.NotNull(quote);
        Assert.Equal(expectedRouteName, quote.Route.ToString());
        Assert.Equal(expectedFee, quote.Fee);
        Assert.Equal(1000, quote.ChargeableWeightGrams);
    }

    [Fact]
    public void TryQuote_HeavierParcel_AddsOneStepPerHalfKilogramOverBaseWeight()
    {
        // 3000 g trên tuyến liên vùng (khối lượng gốc 2000 g): vượt 1000 g là 2 bước, 40.000 + 2 x 7.000.
        var quote = FallbackFeeQuoter.TryQuote(FullConfig(), HaNoi, HoChiMinh, new Parcel(3000, 10, 10, 10));

        Assert.NotNull(quote);
        Assert.Equal(54_000, quote.Fee);
        Assert.Equal(3000, quote.ChargeableWeightGrams);
    }

    [Fact]
    public void TryQuote_UsesVolumetricWeight_WhenLargerThanActualWeight()
    {
        // Hộp 30 x 20 x 10 cm có cân quy đổi 1200 g, làm tròn thành 1500 g. Nội tỉnh vượt 500 g là 1 bước.
        var quote = FallbackFeeQuoter.TryQuote(FullConfig(), HaNoi, HaNoi, new Parcel(300, 30, 20, 10));

        Assert.NotNull(quote);
        Assert.Equal(1500, quote.ChargeableWeightGrams);
        Assert.Equal(25_000, quote.Fee);
    }

    [Fact]
    public void TryQuote_NoConfigRow_ReturnsNull()
    {
        Assert.Null(FallbackFeeQuoter.TryQuote(null, HaNoi, HaNoi, ThousandGrams));
    }

    [Fact]
    public void TryQuote_Disabled_ReturnsNullEvenWithFullTable()
    {
        var config = FullConfig();
        config.Enabled = false;

        Assert.Null(FallbackFeeQuoter.TryQuote(config, HaNoi, HaNoi, ThousandGrams));
    }

    [Theory]
    [InlineData(UnknownProvince, HaNoi)]
    [InlineData(HaNoi, UnknownProvince)]
    [InlineData(UnknownProvince, AnotherUnknownProvince)]
    // Tuyến không xác định thì không đoán tuyến, vì đoán sai là báo sai phí cho khách.
    public void TryQuote_RouteUnknown_ReturnsNull(string pickupProvince, string receiverProvince)
    {
        Assert.Null(FallbackFeeQuoter.TryQuote(FullConfig(), pickupProvince, receiverProvince, ThousandGrams));
    }

    [Theory]
    [InlineData(HaNoi, HaNoi, "same-province")]
    [InlineData(HaNoi, HaiPhong, "same-region")]
    [InlineData(HaNoi, HoChiMinh, "cross-region")]
    // Dòng bật nhưng tuyến thiếu giá chỉ có thể do dữ liệu hỏng, vì API chặn lưu. Không dùng giá thiếu, và không đoán giá.
    public void TryQuote_RouteTierMissingAValue_ReturnsNull(string pickupProvince, string receiverProvince, string routeName)
    {
        var config = FullConfig();
        switch (routeName)
        {
            case "same-province":
                config.SameProvinceStepFee = null;
                break;
            case "same-region":
                config.SameRegionBaseFee = null;
                break;
            case "cross-region":
                config.CrossRegionBaseWeightGrams = null;
                break;
        }

        Assert.Null(FallbackFeeQuoter.TryQuote(config, pickupProvince, receiverProvince, ThousandGrams));
    }

    [Fact]
    public void TryQuote_MissingValueOfAnotherRoute_DoesNotAffectThisRoute()
    {
        var config = FullConfig();
        config.CrossRegionBaseFee = null;

        var quote = FallbackFeeQuoter.TryQuote(config, HaNoi, HaNoi, ThousandGrams);

        Assert.NotNull(quote);
        Assert.Equal(20_000, quote.Fee);
    }

    [Fact]
    public void TryQuote_FeeBeyondInt32_ReturnsNull()
    {
        // 2000 x 2000 x 2000 cm cho cân quy đổi 1,6 tỷ g. Phí vượt `int` nên `Quote.Fee` không giữ được.
        var config = FullConfig();
        config.SameProvinceBaseFee = 5_000_000;
        config.SameProvinceStepFee = 5_000_000;

        Assert.Null(FallbackFeeQuoter.TryQuote(config, HaNoi, HaNoi, new Parcel(1, 2000, 2000, 2000)));
    }
}
