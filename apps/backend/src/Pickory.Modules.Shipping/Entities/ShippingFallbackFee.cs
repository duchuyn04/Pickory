namespace Pickory.Modules.Shipping.Entities;

// Bảng phí dự phòng của shop, bảng chỉ có một dòng. Mỗi tuyến có giá gốc, khối lượng gốc và giá bước.
public sealed class ShippingFallbackFee
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    // Giá tính bằng đồng, khối lượng tính bằng gam. Cột giá chỉ được trống khi `Enabled` là `false`.
    public int? SameProvinceBaseFee { get; set; }
    public int? SameProvinceBaseWeightGrams { get; set; }
    public int? SameProvinceStepFee { get; set; }
    public int? SameRegionBaseFee { get; set; }
    public int? SameRegionBaseWeightGrams { get; set; }
    public int? SameRegionStepFee { get; set; }
    public int? CrossRegionBaseFee { get; set; }
    public int? CrossRegionBaseWeightGrams { get; set; }
    public int? CrossRegionStepFee { get; set; }
    public DateTime UpdatedAt { get; set; }
}
