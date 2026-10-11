using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Contracts.Events;

// Phát sau khi đơn mới đã commit. `UserId` là null với khách vãng lai.
public sealed record OrderPlacedEvent(
    string OrderId,
    string Code,
    string? UserId,
    string AnonymousId,
    int Total,
    PaymentMethod PaymentMethod) : IDomainEvent;
