using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Xóa số điện thoại và ghi chú trên yêu cầu gọi lại khi khách xóa tài khoản, giữ dòng cho báo cáo.
internal sealed class CallbackRequestUserDeletedHandler(SupportDeskDbContext database)
    : IDomainEventHandler<UserDeletedEvent>
{
    public async Task HandleAsync(UserDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        // Quét cả dòng gửi lúc vãng lai thuộc hội thoại sau đó đã gắn vào tài khoản của người dùng này.
        await database.CallbackRequests
            .Where(request => request.CustomerUserId == @event.UserId
                || (request.ConversationId != null
                    && database.Conversations.Any(conversation =>
                        conversation.Id == request.ConversationId
                        && conversation.CustomerUserId == @event.UserId)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(request => request.Phone, (string?)null)
                    .SetProperty(request => request.Note, (string?)null),
                cancellationToken);
    }
}
