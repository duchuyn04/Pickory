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
    DateTime? UpdatedAt,
    CommerceShopView Shop)
{
    public static CommerceSettingsView From(CommerceSettings row) => new(
        row.FreeShippingThreshold,
        row.CodMaxAmount,
        row.MaxQuantityPerSku,
        row.MaxCartLines,
        row.SepayAccountNumber,
        row.SepayBankCode,
        row.UpdatedAt,
        new CommerceShopView(row.ShopName, row.ShopAddress, row.ShopPhone, row.ShopTaxCode));

    // Shop chưa lưu cấu hình thì hiện giá trị mặc định. Hai trường SePay, `UpdatedAt` và các trường shop là `null`.
    public static CommerceSettingsView FromDefaults() => new(
        CommerceLimits.Defaults.FreeShippingThreshold,
        CommerceLimits.Defaults.CodMaxAmount,
        CommerceLimits.Defaults.MaxQuantityPerSku,
        CommerceLimits.Defaults.MaxCartLines,
        null,
        null,
        null,
        CommerceShopView.Empty);
}

// Khối thông tin shop trả ra ngoài trong API cấu hình và trong nhật ký thao tác.
internal sealed record CommerceShopView(
    string? Name,
    string? Address,
    string? Phone,
    string? TaxCode)
{
    public static CommerceShopView Empty { get; } = new(null, null, null, null);
}
