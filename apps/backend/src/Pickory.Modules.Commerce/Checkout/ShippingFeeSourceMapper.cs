using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Shipping.Contracts.Quotes;

namespace Pickory.Modules.Commerce.Checkout;

// Đổi nguồn phí của module Shipping sang nguồn phí ghi trên đơn. Commerce giữ enum riêng để lưu DB, không phụ thuộc enum của Shipping.
internal static class ShippingFeeSourceMapper
{
    public static ShippingFeeSource FromQuote(QuoteSource source) => source switch
    {
        QuoteSource.Ghn => ShippingFeeSource.Ghn,
        QuoteSource.Cached => ShippingFeeSource.Cached,
        QuoteSource.Fallback => ShippingFeeSource.Fallback,
        // Giá trị mới của Shipping mà chưa được ánh xạ phải làm đặt đơn dừng, thay vì ghi sai nguồn phí lên đơn.
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Nguồn phí chưa được ánh xạ."),
    };
}
