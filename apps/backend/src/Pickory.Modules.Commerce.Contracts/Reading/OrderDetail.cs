using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Contracts.Reading;

// Chi tiết đơn hàng tối giản dữ liệu cá nhân phục vụ trợ lý ảo và các module khác.
public sealed record OrderDetail(
    string Code,
    string? UserId,
    string AnonymousId,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod,
    int Subtotal,
    int ShippingFee,
    int ShippingDiscount,
    int Total,
    DateTime CreatedAt,
    DateTime? PaymentDeadline,
    IReadOnlyList<OrderLine> Items);
