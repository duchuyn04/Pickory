using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.SupportDesk;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-09: `PUT /api/support/conversations/{id}/csat` lưu điểm 1 đến 5 cho phiên vừa kết thúc.
[Collection(SupportDeskCollection.Name)]
public sealed class CustomerCsatEndpointTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string FixtureFileName = "customer-api.json";
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // Mốc thời gian cố định cho phiên hỗ trợ trong test.
    private static readonly DateTime BaseTime = new(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => Task.CompletedTask;

    // Dọn dẹp các hội thoại đã tạo sau mỗi test.
    public Task DisposeAsync() => SupportConversationTestKit.DeleteIssuedConversationsAsync(database);

    private static string CsatPath(string conversationId) =>
        $"/api/support/conversations/{conversationId}/csat";

    // Tạo request PUT với đối tượng body và cookie của người gọi.
    private static HttpRequestMessage PutJson(string path, string cookie, object? body) =>
        ActorHttp.CreateJsonRequest(HttpMethod.Put, path, body, cookie);

    // Gửi chuỗi JSON nguyên bản để kiểm tra các trường hợp dữ liệu sai kiểu.
    private static HttpRequestMessage PutRaw(string path, string cookie, string json)
    {
        var request = ActorHttp.CreateRequest(HttpMethod.Put, path, cookie);
        // Header `Origin` là bắt buộc cho các route ghi của khách.
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    [Fact]
    // Người không phải chủ hội thoại nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND`.
    public async Task Put_NonOwnerGuest_Returns404ConversationNotFound()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var strangerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(ownerCookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10));

        // Người lạ gửi điểm đánh giá cho hội thoại của chủ khác.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), strangerCookie, new { score = 5 }));

        // Hệ thống không để lộ sự tồn tại của hội thoại người khác.
        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, SupportDeskErrorCodes.ConversationNotFound);
    }

    [Fact]
    // Khách đăng nhập khác không được chấm điểm hội thoại không thuộc sở hữu.
    public async Task Put_NonOwnerCustomer_Returns404ConversationNotFound()
    {
        var (customerA, cookieA) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var (_, cookieB) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookieA.Split("; ")[1]), customerUserId: customerA.Id);
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10));

        // Khách B gửi điểm cho hội thoại của khách A.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookieB, new { score = 5 }));

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, SupportDeskErrorCodes.ConversationNotFound);
    }

    [Fact]
    // Mã hội thoại không tồn tại nhận 404 `SUPPORT_CONVERSATION_NOT_FOUND` giống hệt hội thoại của người khác.
    public async Task Put_NonExistentConversation_Returns404WithSameBodyAsForeignConversation()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var foreignConversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(ownerCookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database, foreignConversation, BaseTime, closedAt: BaseTime.AddMinutes(10));
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var nonExistentId = Guid.NewGuid().ToString();

        // Gửi điểm cho mã hội thoại ngẫu nhiên và hội thoại của người khác.
        var response = await _client.SendAsync(PutJson(CsatPath(nonExistentId), cookie, new { score = 5 }));
        var foreignResponse = await _client.SendAsync(PutJson(CsatPath(foreignConversation.Id), cookie, new { score = 5 }));

        var unknownBody = await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, SupportDeskErrorCodes.ConversationNotFound);
        var foreignBody = await SupportConversationTestKit.AssertErrorAsync(
            foreignResponse, HttpStatusCode.NotFound, SupportDeskErrorCodes.ConversationNotFound);
        // Nội dung lỗi phải trùng khớp để người ngoài không dò được hội thoại có tồn tại.
        Assert.Equal(unknownBody, foreignBody);
    }

    [Fact]
    // Nhân viên gọi route phía khách nhận 403 `FORBIDDEN`.
    public async Task Put_StaffActor_Returns403Forbidden()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(ownerCookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10));

        // Nhân viên gọi route dành riêng cho khách.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), staffCookie, new { score = 5 }));

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.Forbidden, SupportDeskErrorCodes.Forbidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    // Điểm ngoài khoảng 1 đến 5 trả 400 `VALIDATION_ERROR`.
    public async Task Put_ScoreOutOfRange_Returns400ValidationError(int invalidScore)
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10));

        // Gửi điểm vượt ngoài giới hạn 1 đến 5.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = invalidScore }));

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("{\"score\": null}")]
    [InlineData("{\"score\": \"5\"}")]
    [InlineData("{\"score\": 3.5}")]
    [InlineData("{}")]
    [InlineData("{\"score\": [5]}")]
    [InlineData("{\"score\": {}}")]
    [InlineData("null")]
    // Dữ liệu điểm sai định dạng hoặc thiếu trường trả 400 `VALIDATION_ERROR` thay vì 500.
    public async Task Put_MalformedScore_Returns400ValidationError(string malformedJson)
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10));

        // Gửi body JSON không đúng cấu trúc điểm số.
        var response = await _client.SendAsync(PutRaw(CsatPath(conversation.Id), cookie, malformedJson));

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    // Hội thoại chưa có phiên nào trả 409 `SUPPORT_SESSION_OPEN`.
    public async Task Put_NoSessionInConversation_Returns409SessionOpen()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));

        // Chấm điểm khi hội thoại chưa từng chuyển cho nhân viên.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 5 }));

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.Conflict, "SUPPORT_SESSION_OPEN");
    }

    [Fact]
    // Hội thoại chỉ có phiên đang chờ trả 409 `SUPPORT_SESSION_OPEN`.
    public async Task Put_OnlyWaitingSession_Returns409SessionOpen()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        // Phiên chưa đóng vì `ClosedAt` rỗng.
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: null);

        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 5 }));

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.Conflict, "SUPPORT_SESSION_OPEN");
    }

    [Fact]
    // Hội thoại có phiên đang được xử lý chưa đóng trả 409 `SUPPORT_SESSION_OPEN`.
    public async Task Put_OnlyAssignedSession_Returns409SessionOpen()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        // Phiên đã được nhận nhưng chưa kết thúc.
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, staffId: "staff-1", claimedAt: BaseTime.AddMinutes(1), closedAt: null);

        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 4 }));

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.Conflict, "SUPPORT_SESSION_OPEN");
    }

    [Fact]
    // Gửi điểm hợp lệ trả 200, cập nhật `Csat` và trả header `no-store`.
    public async Task Put_ValidScore_UpdatesLatestClosedSessionAndReturns200()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10));

        // Khách gửi điểm 5 cho phiên vừa kết thúc.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 5 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Header `no-store` ngăn proxy và trình duyệt lưu đệm dữ liệu riêng tư.
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(session.Id, data.GetProperty("sessionId").GetString());
        Assert.Equal(5, data.GetProperty("score").GetInt32());

        // Kiểm tra dữ liệu thực tế đã lưu trong cơ sở dữ liệu.
        await using var context = database.CreateSupportDeskContext();
        var updatedSession = await context.Sessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(5, updatedSession.Csat);
    }

    [Fact]
    // Gửi lại điểm cho cùng phiên ghi đè điểm cũ và phiên chỉ có một điểm.
    public async Task Put_ResubmitScore_OverwritesExistingCsatScore()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10), csat: 3);

        // Khách gửi lại điểm 5 thay cho điểm 3 đã gửi trước đó.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 5 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(session.Id, data.GetProperty("sessionId").GetString());
        Assert.Equal(5, data.GetProperty("score").GetInt32());

        // Xác nhận cơ sở dữ liệu đã cập nhật điểm mới và không tạo phiên khác.
        await using var context = database.CreateSupportDeskContext();
        var sessions = await context.Sessions.Where(s => s.ConversationId == conversation.Id).ToListAsync();
        var singleSession = Assert.Single(sessions);
        Assert.Equal(5, singleSession.Csat);
    }

    [Fact]
    // Hội thoại có nhiều phiên đóng thì chấm cho phiên đóng gần nhất.
    public async Task Put_MultipleClosedSessions_RatesMostRecentClosedSession()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        var olderSession = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(5), csat: 2);
        var newerSession = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime.AddHours(1), closedAt: BaseTime.AddHours(1).AddMinutes(10));

        // Gửi điểm cho hội thoại có hai phiên đã đóng.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 4 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        // Điểm gán vào phiên mới nhất theo `ClosedAt`.
        Assert.Equal(newerSession.Id, data.GetProperty("sessionId").GetString());
        Assert.Equal(4, data.GetProperty("score").GetInt32());

        await using var context = database.CreateSupportDeskContext();
        var storedOlder = await context.Sessions.SingleAsync(s => s.Id == olderSession.Id);
        var storedNewer = await context.Sessions.SingleAsync(s => s.Id == newerSession.Id);
        // Phiên cũ giữ nguyên điểm 2.
        Assert.Equal(2, storedOlder.Csat);
        // Phiên mới nhận điểm 4.
        Assert.Equal(4, storedNewer.Csat);
    }

    [Fact]
    // Phiên mở mới hơn không ngăn việc chấm điểm phiên đóng gần nhất.
    public async Task Put_ClosedSessionWithNewerWaitingSession_RatesClosedSession()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        var closedSession = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10));
        // Một phiên mới vừa mở sau đó và đang chờ.
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime.AddHours(1), closedAt: null);

        // Gửi điểm khi đang có phiên mới mở.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 5 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        // Điểm phải gắn vào phiên đã đóng.
        Assert.Equal(closedSession.Id, data.GetProperty("sessionId").GetString());
        Assert.Equal(5, data.GetProperty("score").GetInt32());
    }

    [Fact]
    // Hội thoại có trạng thái `Closed` vẫn cho phép chấm điểm phiên đã kết thúc.
    public async Task Put_ClosedConversationWithClosedSession_AllowsRating()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie), status: SupportConversationStatus.Closed);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10));

        // Khách đánh giá sau khi hội thoại đã đóng.
        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 5 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(session.Id, data.GetProperty("sessionId").GetString());
        Assert.Equal(5, data.GetProperty("score").GetInt32());
    }

    [Fact]
    // Hợp đồng: response chấm điểm phiên vừa kết thúc khớp `customer-api.json`.
    public async Task Contract_CustomerRatesEndedSession_MatchesExample()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: BaseTime.AddMinutes(10));

        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 5 }));

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        // So khớp response với ví dụ trong file hợp đồng mẫu.
        SupportDeskContract.AssertMatches(body, response.StatusCode, FixtureFileName, "khách chấm điểm phiên vừa kết thúc");
    }

    [Fact]
    // Hợp đồng: response chấm điểm khi chưa kết thúc khớp `customer-api.json`.
    public async Task Contract_CustomerRatesWhenSessionOpen_MatchesExample()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        // Phiên chưa kết thúc.
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, BaseTime, closedAt: null);

        var response = await _client.SendAsync(PutJson(CsatPath(conversation.Id), cookie, new { score = 4 }));

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        // So khớp phản hồi lỗi 409 với ví dụ trong file hợp đồng mẫu.
        SupportDeskContract.AssertMatches(body, response.StatusCode, FixtureFileName, "chấm điểm khi phiên chưa kết thúc");
    }
}
