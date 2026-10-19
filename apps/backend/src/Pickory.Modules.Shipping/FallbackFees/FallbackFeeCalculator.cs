using Pickory.Modules.Shipping.Contracts.Parcels;

namespace Pickory.Modules.Shipping.FallbackFees;

// Giá của một tuyến: giá gốc đã gồm `BaseWeightGrams` đầu tiên, rồi `StepFee` cho mỗi 500 g tiếp theo. Đơn vị tiền là đồng.
internal sealed record FallbackFeeTier(int BaseFee, int BaseWeightGrams, int StepFee);

// Luật thuần tính khối lượng tính cước và phí dự phòng. Mọi phép tính dùng số nguyên 64 bit để không tràn.
internal static class FallbackFeeCalculator
{
    // GHN tính cước theo nấc 500 g.
    public const int StepGrams = 500;

    // Cân quy đổi (gram) bằng `Dài × Rộng × Cao` (cm) chia 5. Công thức này tương đương chia 5000 rồi đổi kg sang gram.
    private const int VolumetricDivisor = 5;

    public static long ChargeableWeightGrams(Parcel parcel)
    {
        // Nhân bằng số 64 bit. Kiện lớn nhân ba cạnh dễ vượt `int`.
        var volumeCubicCm = (long)parcel.LengthCm * parcel.WidthCm * parcel.HeightCm;
        // Làm tròn lên khi chia 5. Phần lẻ của cân quy đổi không được mất, vì sau đó cả số còn được làm tròn lên nấc 500 g.
        var volumetricGrams = CeilingDivide(volumeCubicCm, VolumetricDivisor);

        // GHN lấy số lớn hơn giữa cân thật và cân quy đổi. Hàng nhẹ mà cồng kềnh vẫn bị tính theo thể tích.
        var heavierGrams = Math.Max(parcel.WeightGrams, volumetricGrams);
        // Làm tròn lên bội số của 500 g, giống bảng giá GHN.
        return CeilingDivide(heavierGrams, StepGrams) * StepGrams;
    }

    public static long Fee(FallbackFeeTier tier, long chargeableWeightGrams)
    {
        // Khối lượng không vượt phần đã gồm trong giá gốc thì chỉ trả giá gốc.
        if (chargeableWeightGrams <= tier.BaseWeightGrams)
        {
            return tier.BaseFee;
        }

        // Mỗi 500 g vượt, kể cả phần lẻ, cộng một lần giá bước. Phép chia làm tròn lên giữ phần lẻ.
        var extraSteps = CeilingDivide(chargeableWeightGrams - tier.BaseWeightGrams, StepGrams);
        return tier.BaseFee + extraSteps * tier.StepFee;
    }

    // Chia số nguyên không âm và làm tròn lên. Tránh dùng số thực để không sai số ở giá trị biên.
    private static long CeilingDivide(long value, long divisor) => (value + divisor - 1) / divisor;
}
