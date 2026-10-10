namespace Pickory.Modules.Shipping.Entities;

// Cấu hình lấy hàng của shop, bảng chỉ có một dòng. Token và `ShopId` của GHN nằm trong biến môi trường.
public sealed class ShippingSettings
{
    public string Id { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SenderPhone { get; set; } = string.Empty;
    public string ProvinceCode { get; set; } = string.Empty;
    public string ProvinceName { get; set; } = string.Empty;
    public string WardCode { get; set; } = string.Empty;
    public string WardName { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public int? DefaultServiceTypeId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
