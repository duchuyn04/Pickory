using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Contracts.Events;

// Phát sau khi một dòng bị xóa khỏi giỏ và đã commit.
public sealed record CartItemRemovedEvent(
    string SkuId,
    string? UserId,
    string? AnonymousId) : IDomainEvent;
