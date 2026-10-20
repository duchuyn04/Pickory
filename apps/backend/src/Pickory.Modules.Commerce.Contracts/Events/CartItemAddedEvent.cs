using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Contracts.Events;

// Phát sau khi một dòng được thêm vào giỏ và đã commit. `Source` là chuỗi do client gửi, cho biết nơi thêm.
public sealed record CartItemAddedEvent(
    string SkuId,
    int Quantity,
    string? UserId,
    string? AnonymousId,
    string? Source) : IDomainEvent;
