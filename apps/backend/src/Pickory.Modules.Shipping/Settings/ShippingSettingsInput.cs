namespace Pickory.Modules.Shipping.Settings;

// Giá trị admin gửi lên sau khi đã cắt khoảng trắng và chuẩn hóa số điện thoại. Tên tỉnh và phường chưa có ở đây.
internal sealed record ShippingSettingsInput(
    string SenderName,
    string SenderPhone,
    string ProvinceCode,
    string WardCode,
    string Line,
    int? DefaultServiceTypeId);

// Giá trị đã qua mọi kiểm tra. Tên tỉnh và phường lấy từ danh mục trong DB, không lấy từ body.
internal sealed record ValidatedShippingSettings(ShippingSettingsInput Input, string ProvinceName, string WardName);
