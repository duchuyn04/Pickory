using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-05: `GET` và `PATCH /api/admin/support/callback-requests[/{id}]`. `GET` cần `conversations:read`, `PATCH` cần `conversations:reply`.
[Collection(SupportDeskCollection.Name)]
public sealed class AdminCallbackRequestEndpointTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string AdminPath = CallbackRequestTestKit.AdminCallbackRequestsPath;

    // Mốc 10:00 ngày 2026-11-03 theo giờ Việt Nam. `handledAt` khóa theo đồng hồ giả.
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
        await CallbackRequestTestKit.DeleteIssuedCallbackRequestsAsync(database);
    }

    [Fact]
    // Role sales không có `conversations:read`, nên không đọc được tab Gọi lại.
    public async Task Get_Sales_Returns403()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(database, DefaultRoles.SalesCode, "callback-sales-get");

        var response = await GetAsync($"pk_session={token}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Danh sách xếp `pending` trước, rồi `createdAt` mới nhất trước, rồi `id` để thứ tự ổn định.
    public async Task Get_Support_SortsPendingFirstThenNewestCreatedAtThenId()
    {
        var staffCookie = await CreateSupportCookieAsync();
        var anonymousId = Guid.NewGuid().ToString();
        CallbackRequestTestKit.TrackAnonymousId(anonymousId);
        var oldPending = await SeedAsync(
            anonymousId, CallbackRequestStatus.Pending, TestMoment.AddHours(-2));
        var done = await SeedAsync(
            anonymousId, CallbackRequestStatus.Done, TestMoment.AddHours(-1));
        var newerUnreachable = await SeedAsync(
            anonymousId, CallbackRequestStatus.Unreachable, TestMoment.AddMinutes(-15));
        var newestPending = await SeedAsync(
            anonymousId, CallbackRequestStatus.Pending, TestMoment.AddMinutes(-30));
        // Hai dòng `pending` cùng giờ để kiểm phép phá thế hòa theo `id`.
        var sameTimePending = await SeedAsync(
            anonymousId, CallbackRequestStatus.Pending, TestMoment.AddMinutes(-30));
        var sameTimeFirst = string.CompareOrdinal(newestPending.Id, sameTimePending.Id) <= 0
            ? newestPending
            : sameTimePending;
        var sameTimeSecond = ReferenceEquals(sameTimeFirst, newestPending) ? sameTimePending : newestPending;

        var response = await GetAsync(staffCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        var ids = data.EnumerateArray().Select(element => element.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(
            new[] { sameTimeFirst.Id, sameTimeSecond.Id, oldPending.Id, newerUnreachable.Id, done.Id },
            ids);

        // Phần tử trả về có đúng bảy khóa của hợp đồng, kể cả trường rỗng.
        var first = data.EnumerateArray().First();
        Assert.Equal(
            ["id", "conversationId", "phone", "note", "status", "createdAt", "handledAt"],
            first.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    // Role sales không có `conversations:reply`, nên không đánh dấu được yêu cầu đã gọi.
    public async Task Patch_Sales_Returns403()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(database, DefaultRoles.SalesCode, "callback-sales-patch");

        var response = await PatchAsync(
            $"{AdminPath}/{Guid.NewGuid()}", $"pk_session={token}", new { status = "done" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // `PATCH` cần `conversations:reply`. Người chỉ có `conversations:read` nhận 403 thay vì 400.
    public async Task Patch_StaffWithOnlyConversationsRead_Returns403()
    {
        var role = await IdentityTestData.CreateRoleAsync(database, [Permissions.ConversationsRead]);
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(database, role.Code, "callback-readonly");

        var response = await PatchAsync(
            $"{AdminPath}/{Guid.NewGuid()}", $"pk_session={token}", new { status = "done" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // `status` chỉ nhận `done` hoặc `unreachable`. Giá trị khác, kể cả `pending`, trả 400 với khóa `status`.
    public async Task Patch_StatusNotDoneOrUnreachable_Returns400WithStatusKey()
    {
        var staffCookie = await CreateSupportCookieAsync();
        var anonymousId = Guid.NewGuid().ToString();
        CallbackRequestTestKit.TrackAnonymousId(anonymousId);
        var seeded = await SeedAsync(anonymousId, CallbackRequestStatus.Pending, TestMoment.AddHours(-1));

        var response = await PatchAsync($"{AdminPath}/{seeded.Id}", staffCookie, new { status = "pending" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var error = document!.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var detail = Assert.Single(error.GetProperty("details").EnumerateArray());
        Assert.Equal("status", detail.GetProperty("key").GetString());
    }

    [Fact]
    // Yêu cầu không có trả 404 `CALLBACK_REQUEST_NOT_FOUND`. Không dò được dòng nào tồn tại.
    public async Task Patch_UnknownId_Returns404()
    {
        var staffCookie = await CreateSupportCookieAsync();

        var response = await PatchAsync($"{AdminPath}/{Guid.NewGuid()}", staffCookie, new { status = "done" });

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "CALLBACK_REQUEST_NOT_FOUND");
    }

    [Fact]
    // `PATCH` ghi `handledBy` là mã nhân viên gọi và `handledAt` theo đồng hồ. Response 200 trả yêu cầu đầy đủ.
    public async Task Patch_Done_WritesHandledByAndHandledAtAndReturnsTheFullRequest()
    {
        var (staffId, staffCookie) = await CreateSupportSessionAsync();
        var anonymousId = Guid.NewGuid().ToString();
        CallbackRequestTestKit.TrackAnonymousId(anonymousId);
        var seeded = await SeedAsync(anonymousId, CallbackRequestStatus.Pending, TestMoment.AddHours(-1));

        var response = await PatchAsync($"{AdminPath}/{seeded.Id}", staffCookie, new { status = "done" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(
            ["id", "conversationId", "phone", "note", "status", "createdAt", "handledAt"],
            data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(seeded.Id, data.GetProperty("id").GetString());
        Assert.Equal("done", data.GetProperty("status").GetString());
        Assert.Equal(_clock.GetUtcNow(), data.GetProperty("handledAt").GetDateTimeOffset());

        var stored = await CallbackRequestTestKit.FindCallbackRequestAsync(database, seeded.Id);
        Assert.NotNull(stored);
        Assert.Equal(CallbackRequestStatus.Done, stored!.Status);
        Assert.Equal(staffId, stored.HandledBy);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, stored.HandledAt);
    }

    [Fact]
    // Gọi lại `PATCH` trên yêu cầu đã đánh dấu vẫn được, và ghi đè nhân viên cùng mốc gọi.
    public async Task Patch_AlreadyHandledRequest_OverwritesHandledByAndHandledAt()
    {
        var (firstStaffId, _) = await CreateSupportSessionAsync("callback-handler-a");
        var (secondStaffId, secondCookie) = await CreateSupportSessionAsync("callback-handler-b");
        var anonymousId = Guid.NewGuid().ToString();
        CallbackRequestTestKit.TrackAnonymousId(anonymousId);
        var seeded = await SeedAsync(
            anonymousId, CallbackRequestStatus.Done, TestMoment.AddHours(-2),
            handledBy: firstStaffId, handledAt: TestMoment.AddHours(-1).UtcDateTime);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var response = await PatchAsync($"{AdminPath}/{seeded.Id}", secondCookie, new { status = "unreachable" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await CallbackRequestTestKit.FindCallbackRequestAsync(database, seeded.Id);
        Assert.NotNull(stored);
        Assert.Equal(CallbackRequestStatus.Unreachable, stored!.Status);
        Assert.Equal(secondStaffId, stored.HandledBy);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, stored.HandledAt);
    }

    [Fact]
    // Hai route quản trị trả JSON khớp hai ví dụ trong `admin-callbacks.json`, gồm cả dòng đã xóa số điện thoại.
    public async Task GetAndPatch_Responses_MatchTheContractExamples()
    {
        var (staffId, staffCookie) = await CreateSupportSessionAsync("callback-contract");
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var anonymousId = SupportConversationTestKit.ReadAnonymousId(guestCookie);
        CallbackRequestTestKit.TrackAnonymousId(anonymousId);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, anonymousId);
        var pending = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            anonymousId,
            conversationId: conversation.Id,
            phone: "0901234567",
            note: "Gọi sau 18 giờ giúp mình.",
            status: CallbackRequestStatus.Pending,
            createdAt: TestMoment.AddHours(-1).UtcDateTime);
        await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            anonymousId,
            conversationId: conversation.Id,
            phone: null,
            note: null,
            status: CallbackRequestStatus.Done,
            createdAt: TestMoment.AddDays(-35).UtcDateTime,
            handledBy: staffId,
            handledAt: TestMoment.AddDays(-34).UtcDateTime);

        var listResponse = await GetAsync(staffCookie);
        var listBody = JsonNode.Parse(await listResponse.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(listBody, listResponse.StatusCode, "admin-callbacks.json", "danh sách yêu cầu gọi lại");

        var patchResponse = await PatchAsync($"{AdminPath}/{pending.Id}", staffCookie, new { status = "done" });
        var patchBody = JsonNode.Parse(await patchResponse.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(patchBody, patchResponse.StatusCode, "admin-callbacks.json", "đánh dấu đã gọi");
    }

    private async Task<string> CreateSupportCookieAsync()
    {
        var (_, cookie) = await CreateSupportSessionAsync();
        return cookie;
    }

    private async Task<(string UserId, string Cookie)> CreateSupportSessionAsync(string name = "callback-support")
    {
        var (user, token) = await IdentityTestData.CreateStaffSessionAsync(database, DefaultRoles.SupportCode, name);
        return (user.Id, $"pk_session={token}");
    }

    private Task<CallbackRequest> SeedAsync(
        string anonymousId,
        CallbackRequestStatus status,
        DateTimeOffset createdAt,
        string? handledBy = null,
        DateTime? handledAt = null)
    {
        return CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            anonymousId,
            status: status,
            createdAt: createdAt.UtcDateTime,
            handledBy: handledBy,
            handledAt: handledAt);
    }

    private Task<HttpResponseMessage> GetAsync(string cookie)
    {
        return _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, AdminPath, cookie));
    }

    private Task<HttpResponseMessage> PatchAsync(string path, string cookie, object body)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Patch, path, body, cookie));
    }
}
