using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Shared.Events;

namespace Pickory.Modules.Shipping.Contracts.Events;

// Phát sau khi trạng thái chuẩn của vận đơn đổi và đã commit. `PreviousStatus` là null ở lần ghi trạng thái đầu tiên.
public sealed record ShipmentStatusChangedEvent(
    string ShipmentId,
    string Ref,
    ShipmentStatus Status,
    ShipmentStatus? PreviousStatus,
    DateTimeOffset OccurredAt,
    ShipmentStatusChangeSource Source) : IDomainEvent;
