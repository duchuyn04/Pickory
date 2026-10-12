using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Pricing;
using Pickory.Modules.Commerce.Settings;

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
    DateOnly? ExpectedDeliveryDate);
