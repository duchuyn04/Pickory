using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Contracts.Events;

// Phát sau khi trạng thái đơn đổi và đã commit. Module nghe dùng `Status` để chọn việc cần làm.
public sealed record OrderStatusChangedEvent(
    string OrderId,
    string Code,
    OrderStatus PreviousStatus,
    OrderStatus Status) : IDomainEvent;
