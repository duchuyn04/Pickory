using Pickory.Modules.Shipping.Entities;

namespace Pickory.Modules.Shipping.Settings;

// Cấu hình lấy hàng ở dạng trả ra ngoài và ghi audit. Không có token, URL của GHN hay bí mật webhook.
internal sealed record ShippingSettingsView(
    string SenderName,
    string SenderPhone,
    string ProvinceCode,
    string ProvinceName,
    string WardCode,
    string WardName,
    string Line,
    int? DefaultServiceTypeId,
    DateTime UpdatedAt)
{
    public static ShippingSettingsView From(ShippingSettings row) => new(
        row.SenderName,
        row.SenderPhone,
        row.ProvinceCode,
        row.ProvinceName,
        row.WardCode,
        row.WardName,
        row.Line,
        row.DefaultServiceTypeId,
        row.UpdatedAt);
}
