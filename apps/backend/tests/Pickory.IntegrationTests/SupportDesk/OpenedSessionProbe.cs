using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.IntegrationTests.SupportDesk;

// Handler thử ghi lại việc dòng phiên đã đọc được hay chưa lúc sự kiện phiên được phát.
// Sự kiện phát sau commit thì kết nối khác đọc được dòng phiên; phát trước commit thì không thấy.
public sealed class OpenedSessionProbe(IntegrationDatabaseFixture database)
    : IDomainEventHandler<SupportSessionStatusChangedEvent>
{
    private readonly ConcurrentDictionary<string, bool> _visibilityBySessionId = new();

    // Trạng thái đã ghi của từng phiên. Test đọc sau khi xong request nên không cần chờ thêm.
    public bool? ReadVisibility(string sessionId) =>
        _visibilityBySessionId.TryGetValue(sessionId, out var visible) ? visible : null;

    public async Task HandleAsync(
        SupportSessionStatusChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // Đọc bằng kết nối khác, giống module khác đọc dữ liệu ngay khi nhận sự kiện.
        await using var context = database.CreateSupportDeskContext();
        _visibilityBySessionId[@event.SessionId] = await context.Sessions.AsNoTracking()
            .AnyAsync(session => session.Id == @event.SessionId, cancellationToken);
    }
}
