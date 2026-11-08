using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk;

// Xóa dữ liệu cá nhân của khách trong module khi tài khoản bị xóa: làm sạch yêu cầu gọi lại trước, rồi xóa hội thoại.
internal sealed class SupportUserDeletedHandler(SupportDeskDbContext database)
    : IDomainEventHandler<UserDeletedEvent>
{
    public async Task HandleAsync(UserDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        // Mọi thay đổi chung một giao dịch. Rollback phải giữ cả thông tin gọi lại và hội thoại nếu một bước lỗi.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Xóa số điện thoại và ghi chú của yêu cầu gọi lại trước khi xóa hội thoại.
        // Khóa ngoại `SetNull` sẽ làm mất liên kết của khách vãng lai nếu xóa hội thoại trước.
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

        // Xóa mọi hội thoại của người dùng này. Khóa ngoại cascade ở DB sẽ tự động xóa tin và phiên kèm theo.
        await database.Conversations
            .Where(conversation => conversation.CustomerUserId == @event.UserId)
            .ExecuteDeleteAsync(cancellationToken);
        // Commit một lần sau khi cả hai bước hoàn tất thành công.
        await transaction.CommitAsync(cancellationToken);
    }
}
