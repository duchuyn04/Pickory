using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Contracts.Events;

// Phát sau khi một dòng được thêm vào giỏ và đã commit. `Source` cho biết nơi thêm, ví dụ `shopping-assistant`, để đo hiệu quả AI.
public sealed record CartItemAddedEvent(
    string SkuId,
    int Quantity,
    string? UserId,
    string? AnonymousId,
    string? Source) : IDomainEvent;
