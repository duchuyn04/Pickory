using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Carts;

// Khi khách đăng ký hoặc đăng nhập, gộp giỏ của mã ẩn danh vào giỏ tài khoản.
// Dispatcher bắt lỗi handler và ghi log, nên lỗi gộp giỏ không làm hỏng luồng đăng nhập.
public sealed class CartLinkHandler(CartMergeService mergeService) : IDomainEventHandler<UserLinkedEvent>
{
    public Task HandleAsync(UserLinkedEvent @event, CancellationToken cancellationToken = default)
    {
        return mergeService.MergeGuestCartAsync(@event.AnonymousId, @event.UserId, cancellationToken);
    }
}
