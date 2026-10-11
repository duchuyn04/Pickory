using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Contracts.Events;

// Phát một lần khi đơn được ghi nhận đã thanh toán, sau khi transaction commit. `Amount` là số tiền đã nhận.
public sealed record OrderPaidEvent(
    string OrderId,
    string Code,
    string? UserId,
    string AnonymousId,
    int Amount) : IDomainEvent;
