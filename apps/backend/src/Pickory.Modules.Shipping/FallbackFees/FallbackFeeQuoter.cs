using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.FallbackFees;

// Phí dự phòng đã tính, kèm tuyến và khối lượng tính cước để bên gọi ghi log.
internal sealed record FallbackFeeQuote(int Fee, FallbackFeeRoute Route, long ChargeableWeightGrams);

// Luật thuần quyết định có tính được phí dự phòng hay không, và tính theo bảng của admin. Không đọc DB.
internal static class FallbackFeeQuoter
{
    // Trả `null` khi không được hoặc không thể tính. Bên gọi khi đó giữ lỗi `SHIPPING_UNAVAILABLE`, không đoán phí.
    public static FallbackFeeQuote? TryQuote(
        ShippingFallbackFee? config, string pickupProvinceCode, string receiverProvinceCode, Parcel parcel)
    {
        // Chưa có dòng cấu hình hoặc admin đang tắt thì không dùng phí dự phòng.
        if (config is null || !config.Enabled)
        {
            return null;
        }

        // Không xác định được tuyến thì không đoán tuyến, vì đoán sai là báo sai phí cho khách.
        var route = FallbackFeeRouteRules.Determine(pickupProvinceCode, receiverProvinceCode);
        var tier = FindTier(config, route);
        if (tier is null)
        {
            return null;
        }

        var chargeableWeightGrams = FallbackFeeCalculator.ChargeableWeightGrams(parcel);
        var fee = FallbackFeeCalculator.Fee(tier, chargeableWeightGrams);

        // `Quote.Fee` là `int`. Phí vượt giới hạn đó chỉ xảy ra với kiện vô lý, nên từ chối thay vì cắt bớt phí.
        if (fee > int.MaxValue)
        {
            return null;
        }

        return new FallbackFeeQuote((int)fee, route, chargeableWeightGrams);
    }

    // Trả giá của tuyến, hoặc `null` khi tuyến không xác định hoặc thiếu một trong ba giá trị.
    private static FallbackFeeTier? FindTier(ShippingFallbackFee config, FallbackFeeRoute route)
    {
        // Dòng đã bật luôn đủ chín giá trị vì API chặn lưu thiếu. Kiểm lại ở đây để dữ liệu sửa tay không tạo phí sai.
        return route switch
        {
            FallbackFeeRoute.SameProvince => BuildTier(
                config.SameProvinceBaseFee, config.SameProvinceBaseWeightGrams, config.SameProvinceStepFee),
            FallbackFeeRoute.SameRegion => BuildTier(
                config.SameRegionBaseFee, config.SameRegionBaseWeightGrams, config.SameRegionStepFee),
            FallbackFeeRoute.CrossRegion => BuildTier(
                config.CrossRegionBaseFee, config.CrossRegionBaseWeightGrams, config.CrossRegionStepFee),
            _ => null,
        };
    }

    private static FallbackFeeTier? BuildTier(int? baseFee, int? baseWeightGrams, int? stepFee)
    {
        if (baseFee is null || baseWeightGrams is null || stepFee is null)
        {
            return null;
        }

        return new FallbackFeeTier(baseFee.Value, baseWeightGrams.Value, stepFee.Value);
    }
}
