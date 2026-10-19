namespace Pickory.Modules.Shipping.FallbackFees;

// Giá trị admin gửi lên sau khi đã đọc và kiểm kiểu. Giá `null` nghĩa là admin chưa nhập, và chỉ hợp lệ khi `Enabled` là `false`.
internal sealed record FallbackFeeInput(
    bool Enabled,
    int? SameProvinceBaseFee,
    int? SameProvinceBaseWeightGrams,
    int? SameProvinceStepFee,
    int? SameRegionBaseFee,
    int? SameRegionBaseWeightGrams,
    int? SameRegionStepFee,
    int? CrossRegionBaseFee,
    int? CrossRegionBaseWeightGrams,
    int? CrossRegionStepFee);
