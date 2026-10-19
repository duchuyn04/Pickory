using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.FallbackFees;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.FallbackFees;

// SHIP-13: khối lượng tính cước và phí theo bậc 500 g, dùng số nguyên.
public sealed class FallbackFeeCalculatorTests
{
    [Theory]
    // Cân thật 500 g, hộp nhỏ: đúng bội số của 500 nên giữ nguyên.
    [InlineData(500, 1, 1, 1, 500)]
    // Lệch 1 g thì lên nấc tiếp theo.
    [InlineData(501, 1, 1, 1, 1000)]
    [InlineData(499, 1, 1, 1, 500)]
    [InlineData(1, 1, 1, 1, 500)]
    // Hộp 30 x 20 x 10 cm có cân quy đổi 1200 g, lớn hơn cân thật 300 g. Làm tròn lên thành 1500.
    [InlineData(300, 30, 20, 10, 1500)]
    // Cân quy đổi đúng 1000 g (25 x 20 x 10 = 5000 cm3 / 5) thì giữ 1000.
    [InlineData(300, 25, 20, 10, 1000)]
    // Cân thật lớn hơn cân quy đổi thì lấy cân thật.
    [InlineData(3000, 10, 10, 10, 3000)]
    // Cân thật 0 g vẫn có cân quy đổi 1 cm3 / 5 làm tròn lên, nên tính một nấc 500 g.
    [InlineData(0, 1, 1, 1, 500)]
    public void ChargeableWeight_TakesLargerOfActualAndVolumetricRoundedUpTo500(
        int weightGrams, int length, int width, int height, int expectedGrams)
    {
        var parcel = new Parcel(weightGrams, length, width, height);

        Assert.Equal(expectedGrams, FallbackFeeCalculator.ChargeableWeightGrams(parcel));
    }

    [Fact]
    public void ChargeableWeight_VolumetricWithFraction_RoundsUp()
    {
        // 11 x 10 x 10 = 1100 cm3, chia 5 được 220 g. Cân thật 100 g nhỏ hơn nên lấy 220 rồi lên 500.
        Assert.Equal(500, FallbackFeeCalculator.ChargeableWeightGrams(new Parcel(100, 11, 10, 10)));
        // 7 x 11 x 13 = 1001 cm3, chia 5 được 200,2 g, vẫn dưới 500.
        Assert.Equal(500, FallbackFeeCalculator.ChargeableWeightGrams(new Parcel(100, 7, 11, 13)));
        // 41 x 61 x 1 = 2501 cm3, chia 5 được 500,2 g, vượt 500 một chút nên lên 1000.
        Assert.Equal(1000, FallbackFeeCalculator.ChargeableWeightGrams(new Parcel(100, 41, 61, 1)));
    }

    [Theory]
    // Khối lượng gốc 1000 g, tính cước 1000 g: chỉ giá gốc.
    [InlineData(1000, 1000, 10_000, 3_000, 10_000)]
    // Tính cước 1500 g: giá gốc cộng 1 lần giá bước.
    [InlineData(1000, 1500, 10_000, 3_000, 13_000)]
    // Khối lượng gốc 1200 g, tính cước 1500 g: phần lẻ 300 g tính một bước.
    [InlineData(1200, 1500, 10_000, 3_000, 13_000)]
    // Tính cước 3000 g: 2000 g vượt là 4 bước.
    [InlineData(1000, 3000, 10_000, 3_000, 22_000)]
    // Dưới khối lượng gốc vẫn chỉ trả giá gốc.
    [InlineData(2000, 500, 10_000, 3_000, 10_000)]
    // Vượt 1 g cũng tính một bước.
    [InlineData(1000, 1001, 10_000, 3_000, 13_000)]
    // Giá bước bằng 0 thì phí luôn là giá gốc.
    [InlineData(1000, 5000, 10_000, 0, 10_000)]
    public void Fee_BaseThenOneStepPerStartedHalfKilogramOverBaseWeight(
        int baseWeightGrams, int chargeableGrams, int baseFee, int stepFee, long expectedFee)
    {
        var tier = new FallbackFeeTier(baseFee, baseWeightGrams, stepFee);

        Assert.Equal(expectedFee, FallbackFeeCalculator.Fee(tier, chargeableGrams));
    }

    [Fact]
    public void Fee_MaximumAllowedValues_DoNotOverflow()
    {
        // Giá tối đa 5.000.000 đồng, khối lượng gốc 1 g, kiện 30 kg: (30000 - 1) / 500 làm tròn lên là 60 bước.
        var tier = new FallbackFeeTier(5_000_000, 1, 5_000_000);

        var fee = FallbackFeeCalculator.Fee(tier, 30_000);

        Assert.Equal(5_000_000L + 60L * 5_000_000L, fee);
    }

    [Fact]
    public void ChargeableWeight_LargeDimensions_DoNotOverflow()
    {
        // 2000 x 2000 x 2000 cm tràn `int` khi nhân. Phép tính phải dùng số 64 bit.
        var weight = FallbackFeeCalculator.ChargeableWeightGrams(new Parcel(1, 2000, 2000, 2000));

        Assert.Equal(1_600_000_000L, weight);
    }
}
