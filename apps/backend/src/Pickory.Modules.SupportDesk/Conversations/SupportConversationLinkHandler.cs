using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Conversations;

// Khi khách đăng ký hoặc đăng nhập, gắn hội thoại vãng lai của `pk_anon` vào tài khoản.
// Dispatcher bắt lỗi handler và ghi log, nên lỗi ở đây không làm hỏng luồng đăng nhập.
internal sealed class SupportConversationLinkHandler(
    SupportDeskDbContext database,
    TimeProvider timeProvider,
    ILogger<SupportConversationLinkHandler> logger)
    : IDomainEventHandler<UserLinkedEvent>
{
    private const int MaxAttempts = 2;

    public async Task HandleAsync(UserLinkedEvent @event, CancellationToken cancellationToken = default)
    {
        // Chỉ hội thoại chưa gắn tài khoản mới được gắn. Hội thoại đã thuộc tài khoản khác (người thứ hai đăng nhập chung trình duyệt) giữ nguyên chủ.
        var guestConversationId = await database.Conversations
            .Where(conversation => conversation.AnonymousId == @event.AnonymousId
                && conversation.CustomerUserId == null
                && conversation.Status == SupportConversationStatus.Open)
            .Select(conversation => conversation.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (guestConversationId is null)
        {
            return;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await LinkAsync(guestConversationId, @event, cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Một tab khác của tài khoản vừa tạo hội thoại `open` giữa lúc đọc và lúc lưu. Chỉ bỏ các hội thoại đã nạp, để lần sau đọc lại từ DB mà không gỡ entity khác khỏi context.
                foreach (var entry in database.ChangeTracker.Entries<SupportConversation>().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                if (attempt == MaxAttempts)
                {
                    // Dispatcher nuốt lỗi, nên ghi log tại đây để biết vì sao hội thoại chưa được gắn.
                    logger.LogWarning(
                        exception,
                        "Không gắn được hội thoại {ConversationId} vào tài khoản {UserId} sau {Attempts} lần thử.",
                        guestConversationId,
                        @event.UserId,
                        MaxAttempts);
                }
            }
        }
    }

    private async Task LinkAsync(string guestConversationId, UserLinkedEvent @event, CancellationToken cancellationToken)
    {
        // Đóng và gắn chủ phải cùng một transaction. Lỗi giữa chừng không để lại hội thoại đã đóng mà chưa có hội thoại thay thế.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Đọc lại theo `Id` ở mỗi lần thử. Hội thoại có thể đã đổi trạng thái hoặc đã được gắn từ lần thử trước.
        var guestConversation = await database.Conversations.SingleOrDefaultAsync(
            conversation => conversation.Id == guestConversationId
                && conversation.CustomerUserId == null
                && conversation.Status == SupportConversationStatus.Open,
            cancellationToken);
        if (guestConversation is null)
        {
            return;
        }

        var accountConversation = await database.Conversations.SingleOrDefaultAsync(
            conversation => conversation.CustomerUserId == @event.UserId
                && conversation.Status == SupportConversationStatus.Open,
            cancellationToken);
        if (accountConversation is not null)
        {
            // Khách dùng hai máy nên có hai hội thoại `open`. Đóng bên im lặng lâu hơn, và hòa thì đóng bên của tài khoản.
            var older = accountConversation.LastMessageAt <= guestConversation.LastMessageAt
                ? accountConversation
                : guestConversation;
            older.Status = SupportConversationStatus.Closed;
            older.ClosedAt = timeProvider.GetUtcNow().UtcDateTime;
            // Lưu việc đóng trước khi gắn chủ. Chỉ mục unique theo tài khoản sẽ báo trùng nếu hai hội thoại cùng `open`.
            await database.SaveChangesAsync(cancellationToken);
        }

        guestConversation.CustomerUserId = @event.UserId;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
