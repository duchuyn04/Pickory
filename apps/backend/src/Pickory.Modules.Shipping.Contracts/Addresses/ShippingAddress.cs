namespace Pickory.Modules.Shipping.Contracts.Addresses;

// Địa chỉ nhận hàng hai cấp theo danh mục của GHN. Mã là `_id` của GHN ở dạng chuỗi, tên là chuỗi `name` của danh mục.
public sealed record ShippingAddress(string ProvinceCode, string ProvinceName, string WardCode, string WardName, string Line);
