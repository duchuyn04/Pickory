namespace Pickory.Modules.Shipping.Contracts.Parcels;

// Một dòng hàng cần gộp vào kiện. `Parcel` là kích thước của một đơn vị hàng, `Quantity` phải lớn hơn 0.
public sealed record ParcelItem(Parcel Parcel, int Quantity);
