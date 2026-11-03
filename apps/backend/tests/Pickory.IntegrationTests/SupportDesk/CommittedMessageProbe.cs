using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.IntegrationTests.SupportDesk;

// Handler thử ghi lại việc dòng tin đã đọc được hay chưa lúc sự kiện được phát.
// Sự kiện phát sau commit thì kết nối khác đọc được dòng tin; phát trước commit thì không thấy.
public sealed class CommittedMessageProbe(IntegrationDatabaseFixture database)
    : IDomainEventHandler<SupportMessageCreatedEvent>
{
    private readonly ConcurrentDictionary<string, bool> _visibilityByMessageId = new();

    // Trạng thái đã ghi của từng tin. Test đọc sau khi `FlushAsync` xong nên không cần chờ thêm.
    public bool? ReadVisibility(string messageId) =>
        _visibilityByMessageId.TryGetValue(messageId, out var visible) ? visible : null;

    public async Task HandleAsync(SupportMessageCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Đọc bằng kết nối khác, giống module khác đọc dữ liệu ngay khi nhận sự kiện.
        await using var context = database.CreateSupportDeskContext();
        _visibilityByMessageId[@event.MessageId] = await context.Messages.AsNoTracking()
            .AnyAsync(message => message.Id == @event.MessageId, cancellationToken);
    }
}
