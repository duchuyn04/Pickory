using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Contracts.Events;

// Loại thay đổi của một đánh giá. Khách viết hoặc sửa phát `Created` và `Updated`, còn kiểm duyệt phát `Hidden` và `Shown`.
public enum ReviewChange
{
    Created,
    Updated,
    Hidden,
    Shown,
}

// Phát một lần sau khi transaction commit, mỗi khi đánh giá của một sản phẩm đổi. Người nghe tính lại điểm hoặc xóa cache.
public sealed record ReviewChangedEvent(
    string ProductId,
    string ReviewId,
    ReviewChange Change) : IDomainEvent;
