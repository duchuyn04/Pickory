namespace Pickory.Modules.Commerce.Settings;

// Các giới hạn của commerce mà luật nghiệp vụ đọc. Số tiền là đồng Việt Nam.
public sealed record CommerceLimits(
    int FreeShippingThreshold,
    int CodMaxAmount,
    int MaxQuantityPerSku,
    int MaxCartLines)
{
    // Giá trị dùng khi bảng `commerce_settings` chưa có dòng nào. Khớp mặc định trong `tasks/commerce/plan.md`.
    public static CommerceLimits Defaults { get; } = new(
        FreeShippingThreshold: 500_000,
        CodMaxAmount: 20_000_000,
        MaxQuantityPerSku: 5,
        MaxCartLines: 20);
}
