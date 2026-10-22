using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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

// SD-03: `PUT` và `GET /api/admin/support/presence`. Đồng hồ giả cho phép so `lastSeenAt` với một giờ biết trước.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class StaffPresenceEndpointTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string PresencePath = "/api/admin/support/presence";

    private readonly FakeTimeProvider _clock = new(SupportDeskTestKit.NowAtMillisecondPrecision());
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportDeskTestKit.ClearPresenceAsync(database);
    }

    [Fact]
    public async Task Put_Guest_Returns401()
    {
        var response = await PutAsync("""{"status":"available"}""", cookie: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Customer_Returns403AndWritesNothing()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Customer, DefaultRoles.CustomerCode);

        var response = await PutAsync("""{"status":"available"}""", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id));
    }

    [Fact]
    public async Task Put_SalesWithoutConversationsReply_Returns403AndWritesNothing()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SalesCode);

        var response = await PutAsync("""{"status":"available"}""", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id));
    }

    [Fact]
    public async Task Put_RoleWithOnlyConversationsRead_Returns403AndWritesNothing()
    {
        // Không role mặc định nào có `conversations:read` mà thiếu `conversations:reply`, nên test dựng role riêng.
        var (user, cookie) = await CreateSessionWithPermissionsAsync(Permissions.ConversationsRead);

        var response = await PutAsync("""{"status":"available"}""", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id));
    }

    [Fact]
    public async Task Get_RoleWithOnlyConversationsRead_Returns403()
    {
        var (_, cookie) = await CreateSessionWithPermissionsAsync(Permissions.ConversationsRead);

        var response = await GetAsync(cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_SupportFirstTime_CreatesTheRowAndReturnsTheNewState()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);

        var response = await PutAsync("""{"status":"available"}""", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(user.Id, data.GetProperty("staffId").GetString());
        Assert.Equal("available", data.GetProperty("status").GetString());
        Assert.Equal(_clock.GetUtcNow(), data.GetProperty("lastSeenAt").GetDateTimeOffset());
        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id);
        Assert.NotNull(row);
        Assert.Equal(StaffPresenceStatus.Available, row.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, row.LastSeenAt);
    }

    [Fact]
    public async Task Put_SecondTime_UpdatesTheSameRowAndLastSeenAt()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        await PutAsync("""{"status":"available"}""", cookie);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var response = await PutAsync("""{"status":"away"}""", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id);
        Assert.NotNull(row);
        Assert.Equal(StaffPresenceStatus.Away, row.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, row.LastSeenAt);
        await using var context = database.CreateSupportDeskContext();
        Assert.Equal(1, context.StaffPresences.Count(value => value.StaffId == user.Id));
    }

    [Theory]
    [InlineData("""{"status":"busy"}""")]
    [InlineData("""{"status":"Available"}""")]
    [InlineData("""{"status":""}""")]
    [InlineData("""{"status":null}""")]
    [InlineData("""{}""")]
    [InlineData("""null""")]
    [InlineData("""{"status":5}""")]
    [InlineData("""{"status":true}""")]
    [InlineData("""{"status":["available"]}""")]
    [InlineData("""{"status":{"value":"available"}}""")]
    public async Task Put_InvalidBody_Returns400ValidationErrorAndWritesNothing(string body)
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);

        var response = await PutAsync(body, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(await SupportDeskTestKit.ReadPresenceRowAsync(database, user.Id));
    }

    [Fact]
    public async Task SetStatus_ClockWithSubMillisecondTicks_ReturnsExactlyTheValueStoredInTheDatabase()
    {
        // 7777 tick là 0,7777 mili giây. Cột DB làm tròn lên mili giây kế, nên giá trị trả về phải được cắt trước khi ghi.
        _clock.Advance(TimeSpan.FromTicks(7777));
        var staffId = $"staff-{Guid.NewGuid():N}";

        await using var scope = _host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<StaffPresenceService>();
        var response = await service.SetStatusAsync(staffId, StaffPresenceStatus.Available, CancellationToken.None);

        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, staffId);
        Assert.NotNull(row);
        Assert.Equal(row.LastSeenAt, response.LastSeenAt);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddTicks(-7777), row.LastSeenAt);
    }

    [Fact]
    public async Task RecordHeartbeat_ClockWithSubMillisecondTicks_StoresTheTimeCutToMilliseconds()
    {
        var staffId = $"staff-{Guid.NewGuid():N}";
        await SupportDeskTestKit.SetPresenceRowAsync(database, staffId, StaffPresenceStatus.Available, _clock.GetUtcNow());
        var startedAt = _clock.GetUtcNow().UtcDateTime;
        _clock.Advance(TimeSpan.FromTicks(7777));

        await using var scope = _host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<StaffPresenceService>();
        await service.RecordHeartbeatAsync(staffId, CancellationToken.None);

        // Giờ cắt bằng đúng giờ bắt đầu vì 0,7777 mili giây bị bỏ. Nếu không cắt, DB làm tròn lên thành +1 mili giây.
        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, staffId);
        Assert.NotNull(row);
        Assert.Equal(startedAt, row.LastSeenAt);
    }

    [Fact]
    public async Task Put_TenConcurrentFirstRequestsOfOneStaff_AllReturn200AndLeaveOneRow()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => PutAsync("""{"status":"available"}""", cookie)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await using var context = database.CreateSupportDeskContext();
        Assert.Equal(1, context.StaffPresences.Count(value => value.StaffId == user.Id));
    }

    [Fact]
    public async Task Put_AfterTheRowIsWritten_BroadcastsPresenceChangedToOpenConnections()
    {
        var (user, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        var hub = _host.Services.GetRequiredService<SupportEventHub>();
        using var otherStaffConnection = hub.Register("another-staff");

        var response = await PutAsync("""{"status":"available"}""", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(otherStaffConnection.Reader.TryRead(out var supportEvent));
        Assert.Equal(SupportEventNames.PresenceChanged, supportEvent.Name);
        var json = JsonSerializer.SerializeToElement(supportEvent.Data, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(user.Id, json.GetProperty("staffId").GetString());
        Assert.Equal("available", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Put_InvalidBody_BroadcastsNothing()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        var hub = _host.Services.GetRequiredService<SupportEventHub>();
        using var connection = hub.Register("another-staff");

        await PutAsync("""{"status":"busy"}""", cookie);

        Assert.False(connection.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Get_Guest_Returns401()
    {
        var response = await GetAsync(cookie: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SalesWithoutConversationsReply_Returns403()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SalesCode);

        var response = await GetAsync(cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ListsOnlyAvailableStaffWithoutNames()
    {
        var (availableUser, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        var (awayUser, _) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);
        await SupportDeskTestKit.SetPresenceRowAsync(database, availableUser.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());
        await SupportDeskTestKit.SetPresenceRowAsync(database, awayUser.Id, StaffPresenceStatus.Away, _clock.GetUtcNow());

        var response = await GetAsync(cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var item = Assert.Single(document.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(availableUser.Id, item.GetProperty("staffId").GetString());
        Assert.Equal("available", item.GetProperty("status").GetString());
        Assert.Equal(["staffId", "status", "lastSeenAt"], item.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Get_NobodyAvailable_ReturnsEmptyList()
    {
        var (_, cookie) = await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode);

        var response = await GetAsync(cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Empty(document.RootElement.GetProperty("data").EnumerateArray());
    }

    private async Task<(User User, string Cookie)> CreateSessionAsync(UserKind kind, string roleCode)
    {
        var user = await IdentityTestData.CreateUserAsync(
            database, IdentityTestData.NewEmail("presence"), kind, roleCode, emailVerified: true);
        return (user, $"pk_session={await IdentityTestData.CreateSessionAsync(database, user.Id)}");
    }

    private async Task<(User User, string Cookie)> CreateSessionWithPermissionsAsync(params string[] permissions)
    {
        var role = await IdentityTestData.CreateRoleAsync(database, permissions);
        return await CreateSessionAsync(UserKind.Staff, role.Code);
    }

    private async Task<HttpResponseMessage> PutAsync(string jsonBody, string? cookie)
    {
        var request = ActorHttp.CreateRequest(HttpMethod.Put, PresencePath, cookie);
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetAsync(string? cookie)
    {
        return await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, PresencePath, cookie));
    }
}
