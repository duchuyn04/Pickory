namespace Pickory.Modules.Commerce.Checkout;

// Địa chỉ đã chuẩn hóa. Tên tỉnh và phường lấy từ danh mục của shipping, không lấy từ client.
public sealed record CheckoutPreviewAddress(
    string ProvinceCode,
    string ProvinceName,
    string WardCode,
    string WardName,
    string Line);

// Kết quả preview, với mọi số tiền là đồng Việt Nam. `ExpectedDeliveryDate` là `yyyy-MM-dd`, hoặc `null` khi GHN không báo ngày giao.
public sealed record CheckoutPreviewResponse(
    int Subtotal,
    int ShippingFee,
    int ShippingDiscount,
    int CustomerShippingFee,
    int Total,
    int FreeShippingThreshold,
    int CodMaxAmount,
    bool CodAllowed,
    string? ExpectedDeliveryDate,
    CheckoutPreviewAddress Address);
