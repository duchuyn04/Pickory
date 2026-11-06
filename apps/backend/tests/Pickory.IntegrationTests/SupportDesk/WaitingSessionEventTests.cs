using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Modules.SupportDesk.Sessions;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-04: mở phiên phát `session.waiting` tới khách và tới nhân viên sẵn sàng.
// Test đọc luồng SSE thật của host. Sự kiện chỉ đến từ producer thật của route gửi tin.
[Collection(SupportDeskCollection.Name)]
public sealed class WaitingSessionEventTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string StaffEventsPath = "/api/admin/support/events";

    // Ghi thẳng 3 giây theo tiêu chí SD-04, không đọc hằng của code. Code gửi chậm hơn thì test phải đỏ.
    private static readonly TimeSpan StaffNotificationDeadline = TimeSpan.FromSeconds(3);

    private readonly FakeTimeProvider _clock = new(SupportSessionTestKit.InHoursMoment);
    // Handler thử ghi lại việc dòng phiên đã đọc được lúc sự kiện phiên được phát. Test thứ tự commit dùng số này.
    private readonly OpenedSessionProbe _sessionProbe = new(database);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;
    private SupportEventHub _staffHub = null!;
    private CustomerEventHub _customerHub = null!;

    public async Task InitializeAsync()
    {
        // Bảng trực dùng chung cho cả bộ test, nên mỗi test bắt đầu từ danh sách nhân viên rỗng.
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
        {
            // Đăng ký handler thử cùng dispatcher thật, nên nó chạy đúng lúc host phát sự kiện phiên.
            services.AddSingleton(_sessionProbe);
            services.AddSingleton<IDomainEventHandler<SupportSessionStatusChangedEvent>>(
                provider => provider.GetRequiredService<OpenedSessionProbe>());
        });
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
    }

    [Fact]
    public async Task Stream_FirstMessageOpensSession_NotifiesTheAvailableStaffWithinThreeSeconds()
    {
        var (_, staffCookie) = await CreateStaffAsync(StaffPresenceStatus.Available);
        using var staffStream = await SseTestStream.OpenAsync(_client, StaffEventsPath, staffCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_staffHub, 1);
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));

        // Đo từ lúc khách gửi tin. Tiêu chí tính từ mốc đó tới lúc nhân viên nhận được sự kiện.
        var sentAt = Stopwatch.GetTimestamp();
        var response = await SendMessageAsync(cookie, conversation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var received = await staffStream.ReadEventNamedAsync(
            "session.waiting", ReadRemainingDeadline(sentAt));

        var elapsed = Stopwatch.GetElapsedTime(sentAt);
        Assert.True(
            elapsed <= StaffNotificationDeadline,
            $"Sự kiện phiên chờ tới nhân viên sau {elapsed.TotalSeconds:F2} giây, tiêu chí là 3 giây.");
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        // Dữ liệu của sự kiện nhân viên chỉ có mã phiên, vì client đọc lại danh sách hàng chờ.
        Assert.Equal(["sessionId"], received.Data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(session.Id, received.Data.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task Stream_StaffWhoIsAway_DoesNotReceiveTheWaitingEvent()
    {
        var (_, availableCookie) = await CreateStaffAsync(StaffPresenceStatus.Available);
        var (_, awayCookie) = await CreateStaffAsync(StaffPresenceStatus.Away);
        using var availableStream = await SseTestStream.OpenAsync(_client, StaffEventsPath, availableCookie);
        using var awayStream = await SseTestStream.OpenAsync(_client, StaffEventsPath, awayCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_staffHub, 2);
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));

        await SendMessageAsync(cookie, conversation);

        // Nhân viên sẵn sàng nhận sự kiện, nên sự kiện đã đi qua hub trước dòng đánh dấu.
        await availableStream.ReadEventNamedAsync("session.waiting");
        // Dòng đánh dấu gửi tới mọi kết nối. Nhân viên tạm vắng phải nhận nó trước mọi sự kiện khác.
        _staffHub.Broadcast(new SupportEvent("test.marker", new { }));

        var firstForAwayStaff = await awayStream.ReadEventAsync();
        Assert.Equal("test.marker", firstForAwayStaff.Name);
    }

    [Fact]
    public async Task Stream_FirstMessageOutsideHours_ReachesTheCustomerChannelWithTheCallbackForm()
    {
        // 21:00 giờ Việt Nam là ngoài giờ, nên tin hệ thống mang form số điện thoại.
        _clock.SetUtcNow(SupportSessionTestKit.ClosingMoment);
        await CreateStaffAsync(StaffPresenceStatus.Available);
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        using var customerStream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_customerHub, 1);

        await SendMessageAsync(cookie, conversation);

        var waiting = await customerStream.ReadEventNamedAsync("session.waiting");
        Assert.Equal(["sessionId"], waiting.Data.EnumerateObject().Select(property => property.Name));
        var systemMessage = await customerStream.ReadEventNamedAsync("message.created");
        Assert.Equal("system", systemMessage.Data.GetProperty("role").GetString());
        Assert.Equal("callback_form", systemMessage.Data.GetProperty("action").GetString());
        // Kênh khách không nhận gì ngoài dữ liệu công khai của tin.
        Assert.Equal(
            ["id", "role", "body", "action", "createdAt"],
            systemMessage.Data.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Stream_ReminderAfterTheNudgeThreshold_ReachesAvailableStaffAndTheCustomer()
    {
        // Nhân viên phải sẵn sàng trước khi khách gửi tin, nếu không phiên được mở ở nhánh ngoài giờ.
        var (_, availableCookie) = await CreateStaffAsync(StaffPresenceStatus.Available);
        var (_, awayCookie) = await CreateStaffAsync(StaffPresenceStatus.Away);
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        // Khách gửi tin đầu tiên để route mở phiên chờ đúng như lúc chạy thật.
        await SendMessageAsync(cookie, conversation);
        // Qua mốc nhắc lại rồi mới mở kết nối, vì luồng SSE của nhân viên tự đóng sau 5 phút.
        _clock.Advance(await ReadNudgeThresholdAsync());
        using var availableStream = await SseTestStream.OpenAsync(_client, StaffEventsPath, availableCookie);
        using var awayStream = await SseTestStream.OpenAsync(_client, StaffEventsPath, awayCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_staffHub, 2);
        using var customerStream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_customerHub, 1);

        var remindedCount = await RunWaitNudgeProcessorAsync();
        Assert.Equal(1, remindedCount);

        var reminded = await availableStream.ReadEventNamedAsync("session.reminded");
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Equal(session.Id, reminded.Data.GetProperty("sessionId").GetString());
        var reminderMessage = await customerStream.ReadEventNamedAsync("message.created");
        Assert.Equal("callback_form", reminderMessage.Data.GetProperty("action").GetString());
        // Nhân viên tạm vắng không nhận sự kiện nhắc lại. Dòng đánh dấu gửi sau nên nó phải đến trước mọi sự kiện khác.
        _staffHub.Broadcast(new SupportEvent("test.marker", new { }));
        var firstForAwayStaff = await awayStream.ReadEventAsync();
        Assert.Equal("test.marker", firstForAwayStaff.Name);
    }

    [Fact]
    public async Task Stream_WaitingEvent_IsPublishedOnlyAfterTheSessionIsCommitted()
    {
        await CreateStaffAsync(StaffPresenceStatus.Available);
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        using var customerStream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_customerHub, 1);

        await SendMessageAsync(cookie, conversation);
        await customerStream.ReadEventNamedAsync("session.waiting");

        // Phát trước commit thì handler mở kết nối khác không thấy dòng phiên chưa commit.
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.True(_sessionProbe.ReadVisibility(session.Id));
    }

    private async Task<TimeSpan> ReadNudgeThresholdAsync()
    {
        // Mốc nhắc lại đọc từ dòng cấu hình, nên test không viết cứng 10 phút.
        var settings = await SupportSessionTestKit.ReadSettingsAsync(database);
        return TimeSpan.FromMinutes(settings.WaitNudgeMinutes);
    }

    private async Task<int> RunWaitNudgeProcessorAsync()
    {
        // Processor cần scope riêng, giống một vòng của worker chạy nền.
        await using var scope = _host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<WaitNudgeProcessor>();
        return await processor.RemindWaitingSessionsAsync(CancellationToken.None);
    }

    // Tạo nhân viên có quyền `conversations:read` kèm dòng trực, rồi trả cookie phiên của người đó.
    private async Task<(User User, string Cookie)> CreateStaffAsync(StaffPresenceStatus status)
    {
        var (user, token) = await IdentityTestData.CreateStaffSessionAsync(database, DefaultRoles.SupportCode, "waiting-staff");
        await SupportDeskTestKit.SetPresenceRowAsync(database, user.Id, status, _clock.GetUtcNow());
        return (user, $"pk_session={token}");
    }

    // Phần còn lại của hạn chờ 3 giây kể từ lúc khách gửi tin. Máy chậm gửi lâu hơn thì hạn còn lại rất ngắn và test đỏ.
    private static TimeSpan ReadRemainingDeadline(long startedAt)
    {
        var remaining = StaffNotificationDeadline - Stopwatch.GetElapsedTime(startedAt);
        // `CancellationTokenSource` không nhận hạn bằng 0 hoặc âm, nên giữ một mili giây để lần đọc báo hết hạn.
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
    }

    private static string EventsPath(string conversationId) =>
        $"{SupportConversationTestKit.ConversationsPath}/{conversationId}/events";

    private Task<HttpResponseMessage> SendMessageAsync(string cookie, SupportConversation conversation) =>
        _client.SendAsync(SupportConversationTestKit.Post(
            SupportConversationTestKit.MessagesPath(conversation.Id), cookie, new { body = "Mình cần hỗ trợ." }));
}
