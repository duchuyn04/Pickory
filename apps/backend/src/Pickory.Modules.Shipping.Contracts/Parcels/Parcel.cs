namespace Pickory.Modules.Shipping.Contracts.Parcels;

// Một kiện hàng gửi đi. Cân nặng tính bằng gam, kích thước tính bằng cm.
public sealed record Parcel(int WeightGrams, int LengthCm, int WidthCm, int HeightCm);
