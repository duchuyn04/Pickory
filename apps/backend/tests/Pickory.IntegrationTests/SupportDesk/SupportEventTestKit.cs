using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Shared.Events;

namespace Pickory.IntegrationTests.SupportDesk;

// Đóng vai producer của SD-04, SD-06, SD-07 và SD-08 vì các task đó chưa có.
// Mỗi hàm ghi trong transaction thật, gom sự kiện miền rồi chỉ `FlushAsync` sau commit, giống `CatalogTransaction`.
internal static class SupportEventTestKit
{
    // Tạo tin chưa lưu. Producer thật của SD-07 đặt thêm `StaffId`, nhưng kênh khách không được gửi trường đó.
    public static SupportMessage NewMessage(
        string conversationId, SupportMessageRole role, string body, string? action, DateTime createdAt) => new()
        {
            ConversationId = conversationId,
            Role = role,
            Body = body,
            Action = action,
            CreatedAt = createdAt,
        };

    // Sự kiện của một tin đã lưu. `Id` chỉ có sau `SaveChanges` vì cột khóa chính tự sinh.
    private static SupportMessageCreatedEvent NewMessageCreatedEvent(SupportMessage message) => new(
        message.ConversationId,
        message.Id,
        ReadEventRole(message.Role),
        message.Body,
        message.Action,
        message.CreatedAt);

    // Ghi tin trong transaction thật, commit rồi mới phát sự kiện.
    // `afterSaveBeforeCommit` là bước chen của bài kiểm thứ tự commit, chạy sau `SaveChanges` và trước commit.
    public static async Task<SupportMessage> SaveMessageAfterCommitAsync(
        IntegrationDatabaseFixture database,
        IServiceProvider services,
        SupportMessage message,
        Func<Task>? afterSaveBeforeCommit = null)
    {
        await using var context = database.CreateSupportDeskContext();
        // Transaction thật để test phân biệt dữ liệu đã commit với dữ liệu còn mở.
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Messages.Add(message);
        await context.SaveChangesAsync();

        var events = CreateCollector(services);
        // Gom sự kiện trước commit. Hàng đợi chỉ được phát sau khi transaction commit.
        events.Emit(NewMessageCreatedEvent(message));
        // Dòng đánh dấu của bài kiểm thứ tự commit đến khách trước sự kiện của tin.
        if (afterSaveBeforeCommit is not null)
        {
            await afterSaveBeforeCommit();
        }

        await CommitAndPublishAsync(events, transaction);
        return message;
    }

    // Ghi tin trong transaction rồi hủy transaction, giống producer gặp lỗi.
    public static async Task<SupportMessage> SaveMessageAndRollbackAsync(
        IntegrationDatabaseFixture database,
        IServiceProvider services,
        SupportMessage message)
    {
        await using var context = database.CreateSupportDeskContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Messages.Add(message);
        await context.SaveChangesAsync();

        var events = CreateCollector(services);
        events.Emit(NewMessageCreatedEvent(message));

        await transaction.RollbackAsync();
        // Transaction bị hủy thì bỏ hàng đợi, nên tin đã hủy không bao giờ được phát.
        events.Discard();
        // Flush sau `Discard` để chứng minh hàng đợi rỗng, kể cả khi mã ngoài lỡ gọi flush.
        await events.FlushAsync(CancellationToken.None);
        return message;
    }

    // Mở phiên chờ trong transaction thật rồi phát trạng thái `waiting` sau commit.
    public static async Task<SupportSession> OpenSessionAfterCommitAsync(
        IntegrationDatabaseFixture database,
        IServiceProvider services,
        SupportConversation conversation,
        DateTime handoffAt)
    {
        await using var context = database.CreateSupportDeskContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var session = new SupportSession
        {
            ConversationId = conversation.Id,
            HandoffAt = handoffAt,
            HandoffInHours = true,
            CustomerUserId = conversation.CustomerUserId,
            AnonymousId = conversation.AnonymousId,
        };
        context.Sessions.Add(session);
        await context.SaveChangesAsync();

        var events = CreateCollector(services);
        events.Emit(new SupportSessionStatusChangedEvent(
            session.Id, session.ConversationId, SupportSessionStatus.Waiting));
        await CommitAndPublishAsync(events, transaction);
        return session;
    }

    // Gán nhân viên cho phiên trong transaction thật rồi phát trạng thái `assigned` sau commit.
    public static async Task ClaimSessionAfterCommitAsync(
        IntegrationDatabaseFixture database,
        IServiceProvider services,
        string sessionId,
        string staffId,
        DateTime claimedAt)
    {
        await using var context = database.CreateSupportDeskContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var session = await context.Sessions.SingleAsync(row => row.Id == sessionId);
        session.StaffId = staffId;
        session.ClaimedAt = claimedAt;
        await context.SaveChangesAsync();

        var events = CreateCollector(services);
        events.Emit(new SupportSessionStatusChangedEvent(
            session.Id, session.ConversationId, SupportSessionStatus.Assigned));
        await CommitAndPublishAsync(events, transaction);
    }

    // Kết thúc phiên trong transaction thật rồi phát trạng thái `closed` sau commit.
    public static async Task CloseSessionAfterCommitAsync(
        IntegrationDatabaseFixture database,
        IServiceProvider services,
        string sessionId,
        string staffId,
        DateTime closedAt)
    {
        await using var context = database.CreateSupportDeskContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var session = await context.Sessions.SingleAsync(row => row.Id == sessionId);
        session.ClosedAt = closedAt;
        session.ClosedBy = staffId;
        await context.SaveChangesAsync();

        var events = CreateCollector(services);
        events.Emit(new SupportSessionStatusChangedEvent(
            session.Id, session.ConversationId, SupportSessionStatus.Closed));
        await CommitAndPublishAsync(events, transaction);
    }

    // Tên vai trò trong contract sự kiện. Giá trị khớp nhãn Postgres của `SupportMessageRole`.
    // Bài kiểm `customer` dùng tên này để chứng minh handler không phát ngược tin của khách.
    private static string ReadEventRole(SupportMessageRole role) => role switch
    {
        SupportMessageRole.Customer => "customer",
        SupportMessageRole.Staff => SupportMessageCreatedEvent.StaffRole,
        SupportMessageRole.System => SupportMessageCreatedEvent.SystemRole,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Vai trò của tin không có tên công khai."),
    };

    private static DomainEventCollector CreateCollector(IServiceProvider services) =>
        new(services.GetRequiredService<IEventDispatcher>());

    // Commit rồi mới phát. Bốn producer dùng đúng một thứ tự nên sửa sai chỉ ở một chỗ.
    private static async Task CommitAndPublishAsync(DomainEventCollector events, IDbContextTransaction transaction)
    {
        await transaction.CommitAsync();
        await events.FlushAsync(CancellationToken.None);
    }
}
