using System.Data.Common;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.SupportDesk;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-17: khi nhận `UserDeletedEvent`, module làm sạch thông tin trên yêu cầu gọi lại.
// Mọi hội thoại của người dùng đó cũng bị xóa cùng tin nhắn và phiên trong cùng giao dịch.
[Collection(SupportDeskCollection.Name)]
public sealed class SupportUserDeletedHandlerTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTime CreatedAt = new(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
        await CallbackRequestTestKit.DeleteIssuedCallbackRequestsAsync(database);
    }

    [Fact]
    // Xóa hội thoại, tin, phiên và làm sạch thông tin gọi lại của người dùng bị xóa.
    // Dữ liệu của người dùng khác và khách vãng lai phải giữ nguyên vẹn.
    public async Task Dispatch_UserDeleted_ClearsPhoneAndNote_AndDeletesConversationsWithMessagesAndSessions_ForTargetUserOnly()
    {
        var deletedUserId = $"user-{Guid.NewGuid():N}";
        var otherUserId = $"user-{Guid.NewGuid():N}";

        // 1. Dòng gọi lại gắn trực tiếp `CustomerUserId` của tài khoản bị xóa.
        var directAnonymousId = Guid.NewGuid().ToString();
        var directRow = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            directAnonymousId,
            customerUserId: deletedUserId,
            phone: "0901111111",
            note: "Ghi chú của tài khoản.",
            status: CallbackRequestStatus.Done,
            createdAt: CreatedAt,
            handledBy: "staff-1",
            handledAt: CreatedAt.AddMinutes(10));

        // 2. Hội thoại thứ nhất của user bị xóa: tạo lúc vãng lai rồi đăng nhập gắn chủ. Yêu cầu gọi lại gửi lúc chưa đăng nhập.
        var linkedGuestCookie = SupportConversationTestKit.NewGuestCookie();
        var linkedAnonymousId = SupportConversationTestKit.ReadAnonymousId(linkedGuestCookie);
        var linkedConversation = await SupportConversationTestKit.SeedConversationAsync(
            database, linkedAnonymousId, customerUserId: deletedUserId);
        var linkedConversationRow = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            linkedAnonymousId,
            conversationId: linkedConversation.Id,
            customerUserId: null,
            phone: "0902222222",
            note: "Ghi chú lúc chưa đăng nhập.",
            createdAt: CreatedAt);
        var linkedMessage = await SupportConversationTestKit.SeedMessageAsync(
            database, linkedConversation.Id, "Tin nhắn hội thoại 1 của user bị xóa.", CreatedAt);
        var linkedSession = await SupportSessionTestKit.SeedSessionAsync(
            database, linkedConversation, handoffAt: CreatedAt, closedAt: CreatedAt.AddMinutes(30));

        // 3. Hội thoại thứ hai của user bị xóa: hội thoại đã đóng trước đó.
        var secondAnonymousId = Guid.NewGuid().ToString();
        var secondConversation = await SupportConversationTestKit.SeedConversationAsync(
            database, secondAnonymousId, customerUserId: deletedUserId, status: SupportConversationStatus.Closed);
        var secondMessage = await SupportConversationTestKit.SeedMessageAsync(
            database, secondConversation.Id, "Tin nhắn hội thoại 2 của user bị xóa.", CreatedAt.AddHours(-1));
        var secondSession = await SupportSessionTestKit.SeedSessionAsync(
            database, secondConversation, handoffAt: CreatedAt.AddHours(-1), closedAt: CreatedAt.AddMinutes(-30));

        // 4. Hội thoại và yêu cầu gọi lại của người dùng khác (không bị xóa).
        var otherCookie = SupportConversationTestKit.NewGuestCookie();
        var otherAnonymousId = SupportConversationTestKit.ReadAnonymousId(otherCookie);
        var otherConversation = await SupportConversationTestKit.SeedConversationAsync(
            database, otherAnonymousId, customerUserId: otherUserId);
        var otherUserRow = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            otherAnonymousId,
            conversationId: otherConversation.Id,
            customerUserId: otherUserId,
            phone: "0903333333",
            note: "Ghi chú của người khác.",
            createdAt: CreatedAt);
        var otherMessage = await SupportConversationTestKit.SeedMessageAsync(
            database, otherConversation.Id, "Tin nhắn của user khác.", CreatedAt);
        var otherSession = await SupportSessionTestKit.SeedSessionAsync(
            database, otherConversation, handoffAt: CreatedAt);

        // 5. Hội thoại và yêu cầu gọi lại của khách vãng lai (không gắn user).
        var guestOnlyCookie = SupportConversationTestKit.NewGuestCookie();
        var guestOnlyAnonymousId = SupportConversationTestKit.ReadAnonymousId(guestOnlyCookie);
        var guestOnlyConversation = await SupportConversationTestKit.SeedConversationAsync(database, guestOnlyAnonymousId);
        var guestOnlyRow = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            guestOnlyAnonymousId,
            conversationId: guestOnlyConversation.Id,
            customerUserId: null,
            phone: "0904444444",
            note: "Ghi chú khách vãng lai.",
            createdAt: CreatedAt);
        var guestMessage = await SupportConversationTestKit.SeedMessageAsync(
            database, guestOnlyConversation.Id, "Tin nhắn khách vãng lai.", CreatedAt);

        await DispatchUserDeletedAsync(deletedUserId);

        // Dòng gọi lại trực tiếp của user: mất phone và note, các cột khác giữ nguyên.
        var directAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, directRow.Id);
        Assert.NotNull(directAfter);
        Assert.Null(directAfter!.Phone);
        Assert.Null(directAfter.Note);
        Assert.Equal(CallbackRequestStatus.Done, directAfter.Status);
        Assert.Equal(CreatedAt.AddMinutes(10), directAfter.HandledAt);

        // Dòng gọi lại liên kết qua hội thoại của user: mất phone và note, dòng giữ nguyên, `ConversationId` thành `null`.
        var linkedAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, linkedConversationRow.Id);
        Assert.NotNull(linkedAfter);
        Assert.Null(linkedAfter!.Phone);
        Assert.Null(linkedAfter.Note);
        Assert.Null(linkedAfter.ConversationId);

        // Mọi hội thoại của user bị xóa cùng tin và phiên qua cascade.
        await using (var context = database.CreateSupportDeskContext())
        {
            var conversation1After = await context.Conversations.AsNoTracking()
                .SingleOrDefaultAsync(conversation => conversation.Id == linkedConversation.Id);
            Assert.Null(conversation1After);

            var message1After = await context.Messages.AsNoTracking()
                .SingleOrDefaultAsync(message => message.Id == linkedMessage.Id);
            Assert.Null(message1After);

            var session1After = await context.Sessions.AsNoTracking()
                .SingleOrDefaultAsync(session => session.Id == linkedSession.Id);
            Assert.Null(session1After);

            var conversation2After = await context.Conversations.AsNoTracking()
                .SingleOrDefaultAsync(conversation => conversation.Id == secondConversation.Id);
            Assert.Null(conversation2After);

            var message2After = await context.Messages.AsNoTracking()
                .SingleOrDefaultAsync(message => message.Id == secondMessage.Id);
            Assert.Null(message2After);

            var session2After = await context.Sessions.AsNoTracking()
                .SingleOrDefaultAsync(session => session.Id == secondSession.Id);
            Assert.Null(session2After);
        }

        // Người dùng khác không bị ảnh hưởng: hội thoại, tin, phiên và gọi lại còn nguyên.
        var otherAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, otherUserRow.Id);
        Assert.Equal("0903333333", otherAfter!.Phone);
        Assert.Equal("Ghi chú của người khác.", otherAfter.Note);
        Assert.NotNull(await SupportConversationTestKit.ReadConversationAsync(database, otherConversation.Id));
        var otherMessages = await SupportConversationTestKit.ReadMessagesAsync(database, otherConversation.Id);
        Assert.Contains(otherMessages, message => message.Id == otherMessage.Id);
        var otherSessions = await SupportSessionTestKit.ReadSessionsAsync(database, otherConversation.Id);
        Assert.Contains(otherSessions, session => session.Id == otherSession.Id);

        // Khách vãng lai không bị ảnh hưởng: hội thoại, tin và gọi lại còn nguyên.
        var guestAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, guestOnlyRow.Id);
        Assert.Equal("0904444444", guestAfter!.Phone);
        Assert.Equal("Ghi chú khách vãng lai.", guestAfter.Note);
        Assert.NotNull(await SupportConversationTestKit.ReadConversationAsync(database, guestOnlyConversation.Id));
        var guestMessages = await SupportConversationTestKit.ReadMessagesAsync(database, guestOnlyConversation.Id);
        Assert.Contains(guestMessages, message => message.Id == guestMessage.Id);
    }

    [Fact]
    // Khi việc xóa hội thoại gặp lỗi sau khi xóa thông tin gọi lại, transaction phải rollback toàn bộ.
    public async Task Dispatch_UserDeleted_WhenConversationDeletionFails_RollsBackRedaction()
    {
        var targetUserId = $"user-{Guid.NewGuid():N}";
        var anonymousId = Guid.NewGuid().ToString();

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId, customerUserId: targetUserId);

        var callbackRow = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            anonymousId,
            conversationId: conversation.Id,
            customerUserId: targetUserId,
            phone: "0909999999",
            note: "Thông tin cần giữ nếu xóa thất bại.");

        var interceptor = new FailConversationDeleteInterceptor();

        await using var failingHost = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<SupportDeskDbContext>(options =>
                    options.AddInterceptors(interceptor))));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var scope = failingHost.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetServices<IDomainEventHandler<UserDeletedEvent>>()
                .OfType<SupportUserDeletedHandler>().Single();
            await handler.HandleAsync(new UserDeletedEvent(targetUserId), CancellationToken.None);
        });

        // Transaction đã quay lui nên hội thoại vẫn còn nguyên trong DB.
        var conversationAfter = await SupportConversationTestKit.ReadConversationAsync(database, conversation.Id);
        Assert.NotNull(conversationAfter);

        // Thông tin nhạy cảm trên yêu cầu gọi lại cũng được khôi phục, không bị xóa dở dang.
        var callbackAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, callbackRow.Id);
        Assert.NotNull(callbackAfter);
        Assert.Equal("0909999999", callbackAfter!.Phone);
        Assert.Equal("Thông tin cần giữ nếu xóa thất bại.", callbackAfter.Note);
    }

    private async Task DispatchUserDeletedAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
        await dispatcher.PublishAsync(new UserDeletedEvent(userId), CancellationToken.None);
    }

    // Interceptor chặn và ép lỗi câu lệnh DELETE bảng support_conversations để kiểm tra transaction rollback.
    private sealed class FailConversationDeleteInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            FailIfMatch(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            FailIfMatch(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static void FailIfMatch(DbCommand command)
        {
            if (command.CommandText.Contains("support_conversations", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                // Giả lập lỗi DB xảy ra ở bước xóa hội thoại, sau khi bước làm sạch số điện thoại đã chạy.
                throw new InvalidOperationException("Mô phỏng lỗi DB khi xóa hội thoại hỗ trợ.");
            }
        }
    }
}
