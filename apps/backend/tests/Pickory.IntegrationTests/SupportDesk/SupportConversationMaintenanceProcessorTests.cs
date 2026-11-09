using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Callbacks;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-17: job hằng ngày đóng hội thoại im lặng 24 giờ và xóa hội thoại cũ hơn 90 ngày cùng tin và phiên.
// Test gọi thẳng `SupportConversationMaintenanceProcessor` với `FakeTimeProvider` và gỡ worker nền để không chạy tranh chấp lúc khởi động.
[Collection(SupportDeskCollection.Name)]
public sealed class SupportConversationMaintenanceProcessorTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    private WebApplicationFactory<Program> _host = null!;

    public Task InitializeAsync()
    {
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
        {
            // Gỡ các worker tự động để vòng khởi động của host không thay đổi dữ liệu trước lúc test gọi processor.
            var conversationWorker = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(SupportConversationMaintenanceWorker));
            if (conversationWorker is not null)
            {
                services.Remove(conversationWorker);
            }

            var phoneWorker = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(CallbackPhoneRetentionWorker));
            if (phoneWorker is not null)
            {
                services.Remove(phoneWorker);
            }
        });

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
        await CallbackRequestTestKit.DeleteIssuedCallbackRequestsAsync(database);
    }

    [Fact]
    // Hội thoại `open` có `last_message_at` cũ hơn 24 giờ chuyển `closed`. Hội thoại 23 giờ và đúng 24 giờ còn `open`.
    public async Task Maintain_OpenConversationOlderThan24Hours_IsClosed_And23HoursOrExact24Hours_StaysOpen()
    {
        var nowUtc = TestMoment.UtcDateTime;

        // Hội thoại 1: cũ hơn 24 giờ. Hệ thống chuyển sang `closed` và ghi `ClosedAt`.
        var expiredAnonymousId = Guid.NewGuid().ToString();
        var expiredConversation = await SeedConversationWithLastMessageAtAsync(
            expiredAnonymousId,
            status: SupportConversationStatus.Open,
            lastMessageAt: nowUtc.AddHours(-24).AddMinutes(-1));

        // Hội thoại 2: mới 23 giờ trước. Hội thoại giữ nguyên trạng thái `open`.
        var recentAnonymousId = Guid.NewGuid().ToString();
        var recentConversation = await SeedConversationWithLastMessageAtAsync(
            recentAnonymousId,
            status: SupportConversationStatus.Open,
            lastMessageAt: nowUtc.AddHours(-23));

        // Hội thoại 3: đúng tròn 24 giờ trước. Điều kiện strictly `< cutoff` nên vẫn giữ `open`.
        var exactAnonymousId = Guid.NewGuid().ToString();
        var exactConversation = await SeedConversationWithLastMessageAtAsync(
            exactAnonymousId,
            status: SupportConversationStatus.Open,
            lastMessageAt: nowUtc.AddHours(-24));

        // Hội thoại 4: đã đóng sẵn từ trước. Hội thoại giữ nguyên `closed` và không ghi đè `ClosedAt`.
        var alreadyClosedAt = nowUtc.AddHours(-25);
        var alreadyClosedAnonymousId = Guid.NewGuid().ToString();
        var alreadyClosedConversation = await SeedConversationWithLastMessageAtAsync(
            alreadyClosedAnonymousId,
            status: SupportConversationStatus.Closed,
            lastMessageAt: nowUtc.AddHours(-26),
            closedAt: alreadyClosedAt);

        var result = await RunProcessorAsync();

        Assert.True(result.InactiveClosed >= 1);

        // Hội thoại quá 24 giờ phải chuyển `closed` và cập nhật `ClosedAt` là mốc hiện tại.
        var expiredAfter = await SupportConversationTestKit.ReadConversationAsync(database, expiredConversation.Id);
        Assert.Equal(SupportConversationStatus.Closed, expiredAfter.Status);
        Assert.Equal(nowUtc, expiredAfter.ClosedAt);

        // Hội thoại 23 giờ còn `open` và `ClosedAt` vẫn rỗng.
        var recentAfter = await SupportConversationTestKit.ReadConversationAsync(database, recentConversation.Id);
        Assert.Equal(SupportConversationStatus.Open, recentAfter.Status);
        Assert.Null(recentAfter.ClosedAt);

        // Hội thoại đúng tròn 24 giờ còn `open` và `ClosedAt` vẫn rỗng.
        var exactAfter = await SupportConversationTestKit.ReadConversationAsync(database, exactConversation.Id);
        Assert.Equal(SupportConversationStatus.Open, exactAfter.Status);
        Assert.Null(exactAfter.ClosedAt);

        // Hội thoại đã đóng sẵn không bị đổi `ClosedAt`.
        var alreadyClosedAfter = await SupportConversationTestKit.ReadConversationAsync(database, alreadyClosedConversation.Id);
        Assert.Equal(SupportConversationStatus.Closed, alreadyClosedAfter.Status);
        Assert.Equal(alreadyClosedAt, alreadyClosedAfter.ClosedAt);
    }

    [Fact]
    // Hội thoại cũ hơn 90 ngày bị xóa cùng tin và phiên. Hội thoại 89 ngày và đúng 90 ngày còn giữ. Yêu cầu gọi lại giữ nguyên dòng.
    public async Task Maintain_ConversationOlderThan90Days_IsDeletedWithMessagesAndSessions_And89Days_IsPreserved()
    {
        var nowUtc = TestMoment.UtcDateTime;

        // Hội thoại 1: cũ hơn 90 ngày. Hệ thống xóa cùng tin và phiên qua cascade.
        var expiredAnonymousId = Guid.NewGuid().ToString();
        var expiredConversation = await SeedConversationWithLastMessageAtAsync(
            expiredAnonymousId,
            status: SupportConversationStatus.Closed,
            lastMessageAt: nowUtc.AddDays(-90).AddMinutes(-1));

        var expiredMessage = await SupportConversationTestKit.SeedMessageAsync(
            database,
            expiredConversation.Id,
            "Tin nhắn của hội thoại quá hạn.",
            nowUtc.AddDays(-90).AddMinutes(-1));

        var expiredSession = await SupportSessionTestKit.SeedSessionAsync(
            database,
            expiredConversation,
            handoffAt: nowUtc.AddDays(-90).AddMinutes(-1),
            closedAt: nowUtc.AddDays(-90));

        var expiredCallback = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            expiredAnonymousId,
            conversationId: expiredConversation.Id,
            phone: "0901111111",
            note: "Yêu cầu gọi lại thuộc hội thoại 90 ngày.",
            status: CallbackRequestStatus.Done,
            createdAt: nowUtc.AddDays(-90).AddMinutes(-1),
            handledBy: "staff-1",
            handledAt: nowUtc.AddDays(-90));

        // Hội thoại 2: 89 ngày trước. Hội thoại còn giữ nguyên.
        var preservedAnonymousId = Guid.NewGuid().ToString();
        var preservedConversation = await SeedConversationWithLastMessageAtAsync(
            preservedAnonymousId,
            status: SupportConversationStatus.Closed,
            lastMessageAt: nowUtc.AddDays(-89));

        var preservedMessage = await SupportConversationTestKit.SeedMessageAsync(
            database,
            preservedConversation.Id,
            "Tin nhắn của hội thoại 89 ngày.",
            nowUtc.AddDays(-89));

        var preservedSession = await SupportSessionTestKit.SeedSessionAsync(
            database,
            preservedConversation,
            handoffAt: nowUtc.AddDays(-89),
            closedAt: nowUtc.AddDays(-89).AddHours(1));

        var preservedCallback = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            preservedAnonymousId,
            conversationId: preservedConversation.Id,
            phone: "0902222222",
            note: "Yêu cầu gọi lại thuộc hội thoại 89 ngày.",
            status: CallbackRequestStatus.Pending,
            createdAt: nowUtc.AddDays(-89));

        // Hội thoại 3: đúng tròn 90 ngày trước. Điều kiện strictly `< cutoff` nên vẫn còn giữ.
        var exactAnonymousId = Guid.NewGuid().ToString();
        var exactConversation = await SeedConversationWithLastMessageAtAsync(
            exactAnonymousId,
            status: SupportConversationStatus.Closed,
            lastMessageAt: nowUtc.AddDays(-90));

        var exactMessage = await SupportConversationTestKit.SeedMessageAsync(
            database,
            exactConversation.Id,
            "Tin nhắn đúng 90 ngày.",
            nowUtc.AddDays(-90));

        var exactSession = await SupportSessionTestKit.SeedSessionAsync(
            database,
            exactConversation,
            handoffAt: nowUtc.AddDays(-90),
            closedAt: nowUtc.AddDays(-90).AddHours(1));

        var result = await RunProcessorAsync();

        Assert.True(result.ExpiredDeleted >= 1);

        // Hội thoại quá 90 ngày bị xóa khỏi cơ sở dữ liệu.
        await using (var context = database.CreateSupportDeskContext())
        {
            var deletedConversation = await context.Conversations.AsNoTracking()
                .SingleOrDefaultAsync(conversation => conversation.Id == expiredConversation.Id);
            Assert.Null(deletedConversation);

            // Tin nhắn của hội thoại quá hạn bị xóa theo cascade.
            var deletedMessage = await context.Messages.AsNoTracking()
                .SingleOrDefaultAsync(message => message.Id == expiredMessage.Id);
            Assert.Null(deletedMessage);
            var deletedSession = await context.Sessions.AsNoTracking()
                .SingleOrDefaultAsync(session => session.Id == expiredSession.Id);
            Assert.Null(deletedSession);
        }

        // Dòng yêu cầu gọi lại không bị xóa. `ConversationId` chuyển thành `null` theo FK SetNull, các cột số liệu giữ nguyên.
        var callbackAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, expiredCallback.Id);
        Assert.NotNull(callbackAfter);
        Assert.Null(callbackAfter!.ConversationId);
        Assert.Equal("0901111111", callbackAfter.Phone);
        Assert.Equal("Yêu cầu gọi lại thuộc hội thoại 90 ngày.", callbackAfter.Note);
        Assert.Equal(CallbackRequestStatus.Done, callbackAfter.Status);
        Assert.Equal("staff-1", callbackAfter.HandledBy);
        Assert.Equal(nowUtc.AddDays(-90), callbackAfter.HandledAt);

        // Hội thoại 89 ngày còn giữ nguyên cùng tin nhắn, phiên và liên kết gọi lại.
        var preservedAfter = await SupportConversationTestKit.ReadConversationAsync(database, preservedConversation.Id);
        Assert.NotNull(preservedAfter);
        var preservedMessages = await SupportConversationTestKit.ReadMessagesAsync(database, preservedConversation.Id);
        Assert.Contains(preservedMessages, message => message.Id == preservedMessage.Id);
        var preservedSessions = await SupportSessionTestKit.ReadSessionsAsync(database, preservedConversation.Id);
        Assert.Contains(preservedSessions, session => session.Id == preservedSession.Id);
        var preservedCallbackAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, preservedCallback.Id);
        Assert.Equal(preservedConversation.Id, preservedCallbackAfter!.ConversationId);

        // Hội thoại đúng tròn 90 ngày còn giữ nguyên cùng tin nhắn và phiên.
        var exactAfter = await SupportConversationTestKit.ReadConversationAsync(database, exactConversation.Id);
        Assert.NotNull(exactAfter);
        var exactMessages = await SupportConversationTestKit.ReadMessagesAsync(database, exactConversation.Id);
        Assert.Contains(exactMessages, message => message.Id == exactMessage.Id);
        var exactSessions = await SupportSessionTestKit.ReadSessionsAsync(database, exactConversation.Id);
        Assert.Contains(exactSessions, session => session.Id == exactSession.Id);
    }

    [Fact]
    // Xóa hội thoại hết hạn trước khi đóng hội thoại im lặng để không cập nhật thừa dòng sẽ bị xóa ngay sau đó.
    public async Task Maintain_DeletesExpiredBeforeClosingInactive()
    {
        var nowUtc = TestMoment.UtcDateTime;

        // Hội thoại `open` nhưng đã cũ hơn 90 ngày: phải bị xóa, không bị cập nhật thành `closed`.
        var expiredOpenAnonymousId = Guid.NewGuid().ToString();
        var expiredOpenConversation = await SeedConversationWithLastMessageAtAsync(
            expiredOpenAnonymousId,
            status: SupportConversationStatus.Open,
            lastMessageAt: nowUtc.AddDays(-91));

        var result = await RunProcessorAsync();

        Assert.True(result.ExpiredDeleted >= 1);
        Assert.Equal(0, result.InactiveClosed);

        await using var context = database.CreateSupportDeskContext();
        var deletedConversation = await context.Conversations.AsNoTracking()
            .SingleOrDefaultAsync(conversation => conversation.Id == expiredOpenConversation.Id);
        Assert.Null(deletedConversation);
    }

    private async Task<SupportConversation> SeedConversationWithLastMessageAtAsync(
        string anonymousId,
        SupportConversationStatus status,
        DateTime lastMessageAt,
        DateTime? closedAt = null)
    {
        // Ghi nhận mã vào cả hai test kit để dọn sạch bảng hội thoại và bảng gọi lại.
        CallbackRequestTestKit.TrackAnonymousId(anonymousId);
        return await SupportConversationTestKit.SeedConversationAsync(
            database,
            anonymousId,
            status: status,
            lastMessageAt: lastMessageAt,
            closedAt: closedAt);
    }

    private async Task<SupportConversationMaintenanceResult> RunProcessorAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<SupportConversationMaintenanceProcessor>();
        return await processor.MaintainConversationsAsync(CancellationToken.None);
    }
}
