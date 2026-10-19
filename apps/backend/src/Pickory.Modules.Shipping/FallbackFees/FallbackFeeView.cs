using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.FallbackFees;

// Bảng phí dự phòng ở dạng trả ra ngoài và ghi audit. Phẳng, mỗi ô của UI là một trường.
internal sealed record FallbackFeeView(
    bool Enabled,
    int? SameProvinceBaseFee,
    int? SameProvinceBaseWeightGrams,
    int? SameProvinceStepFee,
    int? SameRegionBaseFee,
    int? SameRegionBaseWeightGrams,
    int? SameRegionStepFee,
    int? CrossRegionBaseFee,
    int? CrossRegionBaseWeightGrams,
    int? CrossRegionStepFee,
    DateTime? UpdatedAt)
{
    // Chưa có dòng cấu hình thì phí dự phòng tắt và chưa có giá nào. `GET` trả dạng này, và audit lần lưu đầu dùng làm trạng thái trước.
    public static FallbackFeeView Default { get; } =
        new(false, null, null, null, null, null, null, null, null, null, null);

    public static FallbackFeeView From(ShippingFallbackFee row) => new(
        row.Enabled,
        row.SameProvinceBaseFee,
        row.SameProvinceBaseWeightGrams,
        row.SameProvinceStepFee,
        row.SameRegionBaseFee,
        row.SameRegionBaseWeightGrams,
        row.SameRegionStepFee,
        row.CrossRegionBaseFee,
        row.CrossRegionBaseWeightGrams,
        row.CrossRegionStepFee,
        row.UpdatedAt);
}
