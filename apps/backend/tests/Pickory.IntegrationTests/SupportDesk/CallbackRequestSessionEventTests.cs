using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Callbacks;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-05: yêu cầu gọi lại đóng phiên chờ chưa ai nhận, phát `session.closed` cho khách cùng nhân viên và phát `callback.created`.
// Test đọc luồng sự kiện thật của host để kiểm đúng nơi phát của route gửi yêu cầu.
[Collection(SupportDeskCollection.Name)]
public sealed class CallbackRequestSessionEventTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string StaffEventsPath = "/api/admin/support/events";

    // Mốc 10:00 ngày 2026-11-03 theo giờ Việt Nam. Giờ đóng phiên khóa theo đồng hồ giả.
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    // Log giữ dấu vết phiên tự đóng theo quyết định 19, nên test đọc lại log của chính host.
    private readonly CapturingLoggerProvider _logs = new();
    // Ghi nhận dòng gọi lại đã thấy trên kết nối khác hay chưa ngay lúc sự kiện được phát.
    private readonly CommittedCallbackProbe _callbackProbe = new(database);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;
    private SupportEventHub _staffHub = null!;
    private CustomerEventHub _customerHub = null!;

    public async Task InitializeAsync()
    {
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            {
                services.AddSingleton(_callbackProbe);
                services.AddSingleton<IDomainEventHandler<CallbackRequestCreatedEvent>>(
                    provider => provider.GetRequiredService<CommittedCallbackProbe>());
            })
            .WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(_logs)));
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _staffHub = _host.Services.GetRequiredService<SupportEventHub>();
        _customerHub = _host.Services.GetRequiredService<CustomerEventHub>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportDeskTestKit.ClearPresenceAsync(database);
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
        await CallbackRequestTestKit.DeleteIssuedCallbackRequestsAsync(database);
    }

    [Fact]
    // Phiên chờ chưa ai nhận bị đóng với `closedBy = auto`. Khách và cả nhân viên tạm vắng đều nhận `session.closed`.
    public async Task Post_WaitingSessionWithoutStaff_ClosesSessionWithAutoAndNotifiesCustomerAndEveryStaffConnection()
    {
        var (_, availableCookie) = await CreateStaffAsync(StaffPresenceStatus.Available);
        var (_, awayCookie) = await CreateStaffAsync(StaffPresenceStatus.Away);
        using var availableStream = await OpenStaffStreamAsync(availableCookie);
        using var awayStream = await OpenStaffStreamAsync(awayCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_staffHub, 2);

        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, _clock.GetUtcNow().UtcDateTime);
        using var customerStream = await OpenCustomerStreamAsync(conversation.Id, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_customerHub, 1);

        var response = await PostCallbackAsync(cookie, conversation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var customerEvent = await customerStream.ReadEventNamedAsync("session.closed");
        Assert.Equal(session.Id, customerEvent.Data.GetProperty("sessionId").GetString());
        // Tab Đang chờ của mọi nhân viên có thể đang hiện phiên này, nên cả nhân viên tạm vắng cũng nhận sự kiện.
        var availableEvent = await availableStream.ReadEventNamedAsync("session.closed");
        Assert.Equal(session.Id, availableEvent.Data.GetProperty("sessionId").GetString());
        var awayEvent = await awayStream.ReadEventNamedAsync("session.closed");
        Assert.Equal(session.Id, awayEvent.Data.GetProperty("sessionId").GetString());

        // Mọi kết nối nhân viên, kể cả người tạm vắng, đều nhận `callback.created` với đúng mã yêu cầu.
        var stored = Assert.Single(await CallbackRequestTestKit.ReadCallbackRequestsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie)));
        var createdEvent = await availableStream.ReadEventNamedAsync("callback.created");
        Assert.Equal(
            ["callbackRequestId"],
            createdEvent.Data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(stored.Id, createdEvent.Data.GetProperty("callbackRequestId").GetString());
        var awayCreatedEvent = await awayStream.ReadEventNamedAsync("callback.created");
        Assert.Equal(stored.Id, awayCreatedEvent.Data.GetProperty("callbackRequestId").GetString());
        // Phát trước commit thì kết nối khác chưa thấy dòng yêu cầu gọi lại.
        Assert.True(_callbackProbe.ReadVisibility(stored.Id));

        var closed = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, closed.ClosedAt);
        Assert.Equal("auto", closed.ClosedBy);
        // Đóng phiên lúc này không gửi tin `csat_form` và không ghi tin hệ thống nào.
        Assert.Empty(await SupportConversationTestKit.ReadMessagesByRoleAsync(
            database, conversation.Id, SupportMessageRole.System));

        // Quyết định 19: phiên tự đóng không ghi dòng audit nào, chỉ ghi log `Information`.
        await using var identity = database.CreateIdentityContext();
        Assert.False(await identity.AuditLogs.AsNoTracking().AnyAsync(log => log.TargetId == session.Id));
        var log = Assert.Single(
            _logs.Entries,
            entry => entry.Level == LogLevel.Information
                && entry.Category.EndsWith("CallbackRequestService", StringComparison.Ordinal)
                && entry.Message.Contains("auto", StringComparison.Ordinal));
        Assert.Contains(session.Id, log.Message, StringComparison.Ordinal);
    }

    [Fact]
    // Phiên đã có người nhận vẫn mở sau khi khách gửi yêu cầu gọi lại. Không sự kiện nào tới khách.
    public async Task Post_SessionAlreadyClaimed_KeepsSessionOpenAndEmitsNothing()
    {
        var (staffUserId, _) = await CreateStaffAsync(StaffPresenceStatus.Available);
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, _clock.GetUtcNow().UtcDateTime, staffId: staffUserId);
        using var customerStream = await OpenCustomerStreamAsync(conversation.Id, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_customerHub, 1);

        var response = await PostCallbackAsync(cookie, conversation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Null(session.ClosedAt);
        Assert.Null(session.ClosedBy);
        // Yêu cầu được lưu bình thường.
        Assert.Single(await CallbackRequestTestKit.ReadCallbackRequestsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie)));
        // Khách không nhận sự kiện nào. Hết hạn chờ làm test đỏ nếu một sự kiện lỡ tới.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => customerStream.ReadEventAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    // Yêu cầu bị 429 không lưu dòng nên không phát `callback.created` cho nhân viên.
    public async Task Post_LimitedRequest_DoesNotEmitCallbackCreated()
    {
        var (_, staffCookie) = await CreateStaffAsync(StaffPresenceStatus.Available);
        using var staffStream = await OpenStaffStreamAsync(staffCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_staffHub, 1);

        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        var anonymousId = SupportConversationTestKit.ReadAnonymousId(cookie);
        // Ba dòng cùng ngày giờ Việt Nam với đồng hồ giả, để yêu cầu kế tiếp là yêu cầu thứ tư.
        for (var index = 0; index < 3; index++)
        {
            await CallbackRequestTestKit.SeedCallbackRequestAsync(
                database, anonymousId, conversation.Id, createdAt: _clock.GetUtcNow().UtcDateTime);
        }

        var response = await PostCallbackAsync(cookie, conversation);

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => staffStream.ReadEventNamedAsync("callback.created", TimeSpan.FromSeconds(1)));
    }

    [Fact]
    // Transaction hủy trước lúc commit không được lưu dòng, không đóng phiên và không phát sự kiện nào.
    public async Task Post_TransactionCommitFails_EmitsNeitherSessionClosedNorCallbackCreated()
    {
        var (_, staffCookie) = await CreateStaffAsync(StaffPresenceStatus.Available);
        await using var failingHost = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(new FailCommitInterceptor())));
        using var failingClient = failingHost.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var staffHub = failingHost.Services.GetRequiredService<SupportEventHub>();
        var customerHub = failingHost.Services.GetRequiredService<CustomerEventHub>();

        using var staffStream = await SseTestStream.OpenAsync(failingClient, StaffEventsPath, staffCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(staffHub, 1);

        var cookie = SupportConversationTestKit.NewGuestCookie();
        var anonymousId = SupportConversationTestKit.ReadAnonymousId(cookie);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, anonymousId);
        await SupportSessionTestKit.SeedSessionAsync(database, conversation, _clock.GetUtcNow().UtcDateTime);
        using var customerStream = await SseTestStream.OpenAsync(failingClient, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(customerHub, 1);

        var response = await failingClient.SendAsync(SupportConversationTestKit.Post(
            CallbackRequestTestKit.CallbackRequestsPath, cookie, new { conversationId = conversation.Id, phone = "0901234567" }));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(await CallbackRequestTestKit.ReadCallbackRequestsAsync(database, anonymousId));
        Assert.Null(Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id)).ClosedAt);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => customerStream.ReadEventAsync(TimeSpan.FromMilliseconds(500)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => staffStream.ReadEventNamedAsync("callback.created", TimeSpan.FromMilliseconds(500)));
    }

    private sealed class CommittedCallbackProbe(IntegrationDatabaseFixture database)
        : IDomainEventHandler<CallbackRequestCreatedEvent>
    {
        private readonly ConcurrentDictionary<string, bool> _visibilityById = new();

        public bool? ReadVisibility(string callbackRequestId) =>
            _visibilityById.TryGetValue(callbackRequestId, out var visible) ? visible : null;

        public async Task HandleAsync(
            CallbackRequestCreatedEvent @event, CancellationToken cancellationToken = default)
        {
            // Đọc bằng kết nối riêng để kiểm chứng dòng yêu cầu đã commit trước lúc phát sự kiện.
            await using var context = database.CreateSupportDeskContext();
            _visibilityById[@event.CallbackRequestId] = await context.CallbackRequests.AsNoTracking()
                .AnyAsync(row => row.Id == @event.CallbackRequestId, cancellationToken);
        }
    }

    // Gây lỗi ngay lúc commit để mô phỏng mất kết nối DB sau khi đã chạy `ExecuteUpdateAsync` đóng phiên.
    private sealed class FailCommitInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Giả lập lỗi commit transaction.");
        }
    }

    // Tạo nhân viên hỗ trợ kèm dòng trực, rồi trả cookie phiên của người đó.
    private async Task<(string UserId, string Cookie)> CreateStaffAsync(StaffPresenceStatus status)
    {
        var (user, token) = await IdentityTestData.CreateStaffSessionAsync(database, DefaultRoles.SupportCode, "callback-staff");
        await SupportDeskTestKit.SetPresenceRowAsync(database, user.Id, status, _clock.GetUtcNow());
        return (user.Id, $"pk_session={token}");
    }

    private Task<SseTestStream> OpenStaffStreamAsync(string cookie) =>
        SseTestStream.OpenAsync(_client, StaffEventsPath, cookie);

    private Task<SseTestStream> OpenCustomerStreamAsync(string conversationId, string cookie) =>
        SseTestStream.OpenAsync(_client, EventsPath(conversationId), cookie);

    private static string EventsPath(string conversationId) =>
        $"{SupportConversationTestKit.ConversationsPath}/{conversationId}/events";

    private Task<HttpResponseMessage> PostCallbackAsync(string cookie, SupportConversation conversation) =>
        _client.SendAsync(SupportConversationTestKit.Post(
            CallbackRequestTestKit.CallbackRequestsPath, cookie, new { conversationId = conversation.Id, phone = "0901234567" }));
}
