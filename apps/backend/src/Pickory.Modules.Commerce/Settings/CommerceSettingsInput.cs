namespace Pickory.Modules.Commerce.Settings;

// Cấu hình admin gửi lên sau khi đã cắt khoảng trắng và chuẩn hóa. Hai trường SePay cùng có giá trị hoặc cùng `null`.
internal sealed record CommerceSettingsInput(
    int FreeShippingThreshold,
    int CodMaxAmount,
    int MaxQuantityPerSku,
    int MaxCartLines,
    string? SepayAccountNumber,
    string? SepayBankCode,
    CommerceShopInput? Shop = null);

// Thông tin shop dùng trên hóa đơn in. Khối `Shop` là `null` khi client không gửi trường `shop`.
internal sealed record CommerceShopInput(
    string? Name,
    string? Address,
    string? Phone,
    string? TaxCode);
