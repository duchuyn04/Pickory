using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Modules.SupportDesk.Realtime;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-03: `GET /api/admin/support/events`. Test dùng đồng hồ giả cho nhịp tim và hạn chờ thật 5 giây cho mọi lần đọc luồng.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class SupportEventsEndpointTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string EventsPath = "/api/admin/support/events";

    // Ghi thẳng 30 giây theo yêu cầu, không đọc hằng của code. Nếu code đổi nhịp thì test này phải đỏ.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    // Ghi thẳng 5 phút theo yêu cầu, không đọc hằng của code. Nếu code đổi tuổi kết nối thì test này phải đỏ.
    private static readonly TimeSpan MaxConnectionLifetime = TimeSpan.FromMinutes(5);

    private readonly FakeTimeProvider _clock = new(SupportDeskTestKit.NowAtMillisecondPrecision());
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;
    private SupportEventHub _hub = null!;

    public async Task InitializeAsync()
    {
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _hub = _host.Services.GetRequiredService<SupportEventHub>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportDeskTestKit.ClearPresenceAsync(database);
    }

    [Fact]
    public async Task Get_Guest_Returns401()
    {
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie: null);

        Assert.Equal(HttpStatusCode.Unauthorized, stream.Response.StatusCode);
        Assert.Equal(0, _hub.ConnectionCount);
    }

    [Fact]
    public async Task Get_Customer_Returns403()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Customer, DefaultRoles.CustomerCode);

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);

        Assert.Equal(HttpStatusCode.Forbidden, stream.Response.StatusCode);
        Assert.Equal(0, _hub.ConnectionCount);
    }

    [Fact]
    public async Task Get_SalesWithoutConversationsRead_Returns403AndDoesNotRegisterInTheHub()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SalesCode);

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);

        Assert.Equal(HttpStatusCode.Forbidden, stream.Response.StatusCode);
        Assert.Equal(0, _hub.ConnectionCount);
    }

    [Fact]
    public async Task Get_RoleWithOnlyConversationsReply_Returns403()
    {
        // Không role mặc định nào có `conversations:reply` mà thiếu `conversations:read`, nên test dựng role riêng.
        var role = await IdentityTestData.CreateRoleAsync(database, [Permissions.ConversationsReply]);
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, role.Code);

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);

        Assert.Equal(HttpStatusCode.Forbidden, stream.Response.StatusCode);
        Assert.Equal(0, _hub.ConnectionCount);
    }

    [Fact]
    public async Task Get_Support_Returns200WithTheThreeStreamingHeaders()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);

        Assert.Equal(HttpStatusCode.OK, stream.Response.StatusCode);
        Assert.Equal("text/event-stream", stream.Response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", Assert.Single(stream.Response.Headers.GetValues("Cache-Control")));
        Assert.Equal("no", Assert.Single(stream.Response.Headers.GetValues("X-Accel-Buffering")));
    }

    [Fact]
    public async Task Get_Support_RegistersOneConnectionInTheHubAndRemovesItWhenTheClientDisconnects()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);

        var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);
        stream.Dispose();

        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 0);
    }

    [Fact]
    public async Task Heartbeat_After30Seconds_IsSentAndRefreshesLastSeenAt()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        await SupportDeskTestKit.SetPresenceRowAsync(database, user.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        _clock.Advance(HeartbeatInterval);
        var heartbeat = await stream.ReadEventNamedAsync(SupportEventNames.Heartbeat);

        Assert.Equal(_clock.GetUtcNow().UtcDateTime, heartbeat.Data.GetProperty("at").GetDateTime().ToUniversalTime());
        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id);
        Assert.NotNull(row);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, row.LastSeenAt);
        Assert.Equal(StaffPresenceStatus.Available, row.Status);
    }

    [Fact]
    public async Task Heartbeat_BeforeTheFirst30Seconds_IsNotSent()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        _clock.Advance(HeartbeatInterval - TimeSpan.FromMilliseconds(1));
        // Phát một sự kiện đánh dấu. Nếu nhịp tim đã được gửi sớm thì nó đứng trước sự kiện này.
        _hub.Broadcast(new SupportEvent("test.marker", new { }));

        var first = await stream.ReadEventAsync();
        Assert.Equal("test.marker", first.Name);
    }

    [Fact]
    public async Task Heartbeat_StaffWithoutAPresenceRow_DoesNotCreateARow()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        _clock.Advance(HeartbeatInterval);
        await stream.ReadEventNamedAsync(SupportEventNames.Heartbeat);

        Assert.Null(await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id));
    }

    [Fact]
    public async Task Heartbeat_StaffWhoIsAway_RefreshesLastSeenAtButKeepsAway()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        await SupportDeskTestKit.SetPresenceRowAsync(database, user.Id, StaffPresenceStatus.Away, _clock.GetUtcNow());
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        _clock.Advance(HeartbeatInterval);
        await stream.ReadEventNamedAsync(SupportEventNames.Heartbeat);

        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id);
        Assert.NotNull(row);
        Assert.Equal(StaffPresenceStatus.Away, row.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, row.LastSeenAt);
    }

    [Fact]
    public async Task Stream_PresenceChangedOfAnotherStaff_ReachesTheOpenConnection()
    {
        var (_, listenerCookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        var (changer, changerCookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, listenerCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        var put = ActorHttp.CreateJsonRequest(HttpMethod.Put, "/api/admin/support/presence", new { status = "available" }, changerCookie);
        var response = await _client.SendAsync(put);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = await stream.ReadEventNamedAsync(SupportEventNames.PresenceChanged);
        Assert.Equal(changer.Id, received.Data.GetProperty("staffId").GetString());
        Assert.Equal("available", received.Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task TwoTabs_ClosingOneTab_RemainingTabKeepsTheStaffAvailableBeyondTwoMinutes()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        await SupportDeskTestKit.SetPresenceRowAsync(database, user.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());
        using var closingTab = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        using var remainingTab = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 2);
        closingTab.Dispose();
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        // 5 nhịp, mỗi nhịp 30 giây, là 150 giây. Chờ nhịp tim đến tay client rồi mới tiến tiếp, để mỗi lần ghi DB xong trước nhịp kế.
        for (var beat = 0; beat < 5; beat++)
        {
            _clock.Advance(HeartbeatInterval);
            await remainingTab.ReadEventNamedAsync(SupportEventNames.Heartbeat);
        }

        var changedCount = await RunTimeoutProcessorAsync();
        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id);
        Assert.NotNull(row);
        Assert.Equal(0, changedCount);
        Assert.Equal(StaffPresenceStatus.Available, row.Status);
    }

    [Fact]
    public async Task TwoTabs_ClosingTheLastTab_StaffBecomesAwayAfterTwoMinutesWithoutHeartbeat()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        await SupportDeskTestKit.SetPresenceRowAsync(database, user.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());
        using var onlyTab = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);
        onlyTab.Dispose();
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 0);

        _clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        var changedCount = await RunTimeoutProcessorAsync();

        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id);
        Assert.NotNull(row);
        Assert.Equal(1, changedCount);
        Assert.Equal(StaffPresenceStatus.Away, row.Status);
    }

    [Fact]
    public async Task Stream_AfterTheMaxConnectionLifetime_ClosesNormallyAndLeavesTheHub()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        _clock.Advance(MaxConnectionLifetime);
        var eventsBeforeClose = await stream.ReadUntilStreamEndsAsync();

        // Luồng đọc tới cuối mà không ném lỗi. Máy chủ đóng response bình thường để `EventSource` tự kết nối lại.
        Assert.All(eventsBeforeClose, received => Assert.Equal(SupportEventNames.Heartbeat, received.Name));
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 0);
    }

    [Fact]
    public async Task Stream_OneSecondBeforeTheMaxConnectionLifetime_StaysOpen()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        _clock.Advance(MaxConnectionLifetime - TimeSpan.FromSeconds(1));
        // Sự kiện đánh dấu đến được client nghĩa là luồng còn mở. Nhịp tim có thể đến trước nên đọc tới đúng tên.
        _hub.Broadcast(new SupportEvent("test.marker", new { }));

        await stream.ReadEventNamedAsync("test.marker");
        Assert.Equal(1, _hub.ConnectionCount);
    }

    [Fact]
    public async Task Stream_WhenTheApplicationIsStopping_ClosesNormallyAndLeavesTheHub()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath, cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        _host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await stream.ReadUntilStreamEndsAsync();

        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 0);
    }

    private async Task<int> RunTimeoutProcessorAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<StaffPresenceTimeoutProcessor>();
        return await processor.MarkStaleStaffAwayAsync(CancellationToken.None);
    }

    private async Task<(User User, string Cookie)> CreateSessionAsync(UserKind kind, string roleCode)
    {
        var user = await IdentityTestData.CreateUserAsync(
            database, IdentityTestData.NewEmail("events"), kind, roleCode, emailVerified: true);
        return (user, $"pk_session={await IdentityTestData.CreateSessionAsync(database, user.Id)}");
    }
}
