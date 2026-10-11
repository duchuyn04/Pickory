using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Settings;

// Cấu hình commerce ở dạng trả ra ngoài và ghi audit. Không chứa `SEPAY_WEBHOOK_API_KEY`, vì khóa này không nằm trong bảng.
internal sealed record CommerceSettingsView(
    int FreeShippingThreshold,
    int CodMaxAmount,
    int MaxQuantityPerSku,
    int MaxCartLines,
    string? SepayAccountNumber,
    string? SepayBankCode,
    DateTime? UpdatedAt)
{
    public static CommerceSettingsView From(CommerceSettings row) => new(
        row.FreeShippingThreshold,
        row.CodMaxAmount,
        row.MaxQuantityPerSku,
        row.MaxCartLines,
        row.SepayAccountNumber,
        row.SepayBankCode,
        row.UpdatedAt);

    // Shop chưa lưu cấu hình thì hiện giá trị mặc định. Hai trường SePay và `UpdatedAt` là `null`.
    public static CommerceSettingsView FromDefaults() => new(
        CommerceLimits.Defaults.FreeShippingThreshold,
        CommerceLimits.Defaults.CodMaxAmount,
        CommerceLimits.Defaults.MaxQuantityPerSku,
        CommerceLimits.Defaults.MaxCartLines,
        null,
        null,
        null);
}
