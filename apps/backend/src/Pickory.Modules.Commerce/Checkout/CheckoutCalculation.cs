using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Pricing;
using Pickory.Modules.Commerce.Settings;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Contracts.Parcels;

namespace Pickory.Modules.Commerce.Checkout;

// Kết quả tính tiền của checkout cho một giỏ, dùng chung cho preview và đặt đơn.
// `Snapshots` là dữ liệu SKU lúc tính tiền, tra theo `SkuId`.
public sealed record CheckoutCalculation(
    CheckoutPreviewAddress Address,
    IReadOnlyList<CheckoutLine> Lines,
    IReadOnlyDictionary<string, SkuSnapshot> Snapshots,
    OrderPricingResult Pricing,
    CommerceLimits Limits,
    bool CodAllowed,
    DateOnly? ExpectedDeliveryDate,
    // Địa chỉ và kiện đã dùng để báo phí. Đặt đơn kiểm địa chỉ nhận với đúng dữ liệu này.
    ShippingAddress Destination,
    Parcel Parcel,
    // Nguồn của phí vận chuyển trong `Pricing`. Chỉ đặt đơn dùng giá trị này để ghi lên đơn, và API của khách không trả giá trị này.
    ShippingFeeSource FeeSource);
