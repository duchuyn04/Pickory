namespace Pickory.Modules.Shipping.Contracts.Addresses;

// Tên chuẩn của một cặp tỉnh/thành và phường/xã theo danh mục hiện tại của shipping.
public sealed record AddressNames(string ProvinceName, string WardName);
