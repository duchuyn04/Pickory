using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Contracts.Reading;

// Tóm tắt thông tin đơn hàng trong danh sách lịch sử phục vụ hiển thị nhanh.
public sealed record OrderSummary(
    string Code,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod,
    int Total,
    int ItemCount,
    DateTime CreatedAt);
