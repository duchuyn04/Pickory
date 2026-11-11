using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-05: `POST /api/support/callback-requests`. Khách để lại số điện thoại để nhân viên gọi lại.
[Collection(SupportDeskCollection.Name)]
public sealed class CallbackRequestEndpointTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string CallbackRequestsPath = CallbackRequestTestKit.CallbackRequestsPath;

    // Mốc 10:00 ngày 2026-11-03 theo giờ Việt Nam. Giờ tạo yêu cầu khóa theo đồng hồ giả, không phụ thuộc giờ chạy test.
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
    // Nhân viên gọi route phía khách nhận 403, giống các route hội thoại của SD-15.
    public async Task Post_StaffActor_Returns403()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);

        var response = await PostAsync(staffCookie, new { conversationId = Guid.NewGuid().ToString(), phone = "0901234567" });

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    // `conversationId` trống trả 400 trước khi chạm DB. Khóa lỗi chỉ đúng trường sai.
    public async Task Post_MissingConversationId_Returns400WithConversationIdKey()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();

        var response = await PostRawAsync(cookie, """{"phone":"0901234567"}""");

        await AssertValidationKeyAsync(response, "conversationId");
    }

    [Fact]
    // `phone` để trống là bắt buộc phải chặn, vì `GetPhoneError` coi số trống là hợp lệ.
    public async Task Post_EmptyPhone_Returns400WithPhoneKey()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        var missingResponse = await PostRawAsync(cookie, $$"""{"conversationId":"{{conversation.Id}}"}""");
        await AssertValidationKeyAsync(missingResponse, "phone");

        var emptyResponse = await PostRawAsync(cookie, $$"""{"conversationId":"{{conversation.Id}}","phone":""}""");
        await AssertValidationKeyAsync(emptyResponse, "phone");
    }

    [Fact]
    // Số sai luật của `ContactFieldRules` trả 400. Client gán lỗi vào ô số điện thoại bằng khóa `phone`.
    public async Task Post_InvalidPhone_Returns400WithPhoneKey()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        var response = await PostAsync(cookie, new { conversationId = conversation.Id, phone = "09012345" });

        await AssertValidationKeyAsync(response, "phone");

        // Validator chạy trước bước kiểm chủ, nên hội thoại không tồn tại vẫn trả 400.
        var unknownConversationResponse = await PostAsync(
            cookie, new { conversationId = Guid.NewGuid().ToString(), phone = "09012345" });
        await AssertValidationKeyAsync(unknownConversationResponse, "phone");
    }

    [Fact]
    // Ghi chú quá 500 ký tự trả 400. Giới hạn này nằm ở hợp đồng API.
    public async Task Post_NoteLongerThan500Chars_Returns400WithNoteKey()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        var response = await PostAsync(cookie, new
        {
            conversationId = conversation.Id,
            phone = "0901234567",
            note = new string('x', 501),
        });

        await AssertValidationKeyAsync(response, "note");
    }

    [Fact]
    // Khách vãng lai khác `pk_anon` không phải chủ hội thoại. Không dò được hội thoại nào tồn tại qua route này.
    public async Task Post_GuestWhoDoesNotOwnTheConversation_Returns404()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(ownerCookie);
        var strangerCookie = SupportConversationTestKit.NewGuestCookie();

        var response = await PostAsync(strangerCookie, new { conversationId = conversation.Id, phone = "0901234567" });
        var unknownResponse = await PostAsync(
            strangerCookie, new { conversationId = Guid.NewGuid().ToString(), phone = "0901234567" });

        var foreignBody = await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        var unknownBody = await SupportConversationTestKit.AssertErrorAsync(
            unknownResponse, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        // Phản hồi của hội thoại người khác và mã không tồn tại phải trùng khớp từng ký tự.
        Assert.Equal(unknownBody, foreignBody);
    }

    [Fact]
    // Khách đăng nhập khác tài khoản chủ hội thoại cũng nhận 404, không phải 403.
    public async Task Post_LoggedInCustomerWhoDoesNotOwnTheConversation_Returns404()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(ownerCookie);
        var (_, strangerCookie) = await SupportConversationTestKit.CreateCustomerAsync(database);

        var response = await PostAsync(strangerCookie, new { conversationId = conversation.Id, phone = "0901234567" });

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
    }

    [Fact]
    // Kiểm chủ hội thoại chạy trước giới hạn ngày, nên người lạ hết lượt vẫn nhận 404 thay vì 429.
    public async Task Post_NonOwnerAlreadyAtDailyLimit_Returns404BeforeCheckingLimit()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var ownerConversation = await SeedConversationAsync(ownerCookie);
        var strangerCookie = SupportConversationTestKit.NewGuestCookie();
        var strangerAnonymousId = SupportConversationTestKit.ReadAnonymousId(strangerCookie);
        for (var index = 0; index < 3; index++)
        {
            await CallbackRequestTestKit.SeedCallbackRequestAsync(
                database, strangerAnonymousId, createdAt: _clock.GetUtcNow().UtcDateTime);
        }

        var response = await PostAsync(
            strangerCookie, new { conversationId = ownerConversation.Id, phone = "0901234567" });

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
    }

    [Fact]
    // Số đúng luật được chuẩn hóa về dạng `0xxxxxxxxx` trước khi lưu. Response 201 chỉ có ba khóa theo hợp đồng.
    public async Task Post_InternationalPhone_StoresNormalizedPhoneAndReturns201()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        var response = await PostAsync(
            cookie, new { conversationId = conversation.Id, phone = "+84 901 234 567", note = "Gọi sau 18 giờ." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Dữ liệu cá nhân nên response không được lưu bản sao ở trình duyệt hay proxy.
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(
            ["id", "status", "createdAt"],
            data.EnumerateObject().Select(property => property.Name));
        Assert.Equal("pending", data.GetProperty("status").GetString());
        Assert.Equal(_clock.GetUtcNow(), data.GetProperty("createdAt").GetDateTimeOffset());

        var stored = Assert.Single(await CallbackRequestTestKit.ReadCallbackRequestsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie)));
        Assert.Equal("0901234567", stored.Phone);
        Assert.Equal("Gọi sau 18 giờ.", stored.Note);
        Assert.Equal(CallbackRequestStatus.Pending, stored.Status);
        Assert.Equal(conversation.Id, stored.ConversationId);
        Assert.Equal(SupportConversationTestKit.ReadAnonymousId(cookie), stored.AnonymousId);
        // Khách vãng lai chưa có tài khoản.
        Assert.Null(stored.CustomerUserId);
        Assert.Null(stored.HandledBy);
        Assert.Null(stored.HandledAt);
    }

    [Fact]
    // Chủ tài khoản gửi yêu cầu thì dòng gọi lại gắn mã tài khoản, để `UserDeletedEvent` tìm được dòng này.
    public async Task Post_LoggedInOwner_StoresCustomerUserId()
    {
        var anonymousCookie = SupportConversationTestKit.NewGuestCookie();
        var (user, cookie) = await SupportConversationTestKit.CreateCustomerAsync(database, anonymousCookie);
        var conversation = await SeedConversationAsync(anonymousCookie, customerUserId: user.Id);

        var response = await PostAsync(cookie, new { conversationId = conversation.Id, phone = "0901234567" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = Assert.Single(await CallbackRequestTestKit.ReadCallbackRequestsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(anonymousCookie)));
        Assert.Equal(user.Id, stored.CustomerUserId);
    }

    [Fact]
    // Ghi chú cắt khoảng trắng hai đầu. Chuỗi rỗng sau khi cắt lưu thành `null` thay vì chuỗi rỗng.
    public async Task Post_BlankNote_StoresNullAndTrimsSurroundingWhitespace()
    {
        var blankCookie = SupportConversationTestKit.NewGuestCookie();
        var blankConversation = await SeedConversationAsync(blankCookie);
        var trimmedCookie = SupportConversationTestKit.NewGuestCookie();
        var trimmedConversation = await SeedConversationAsync(trimmedCookie);

        await PostAsync(blankCookie, new { conversationId = blankConversation.Id, phone = "0901234567", note = "   " });
        await PostAsync(
            trimmedCookie, new { conversationId = trimmedConversation.Id, phone = "0901234567", note = "  Gọi sau 18h  " });

        var blankStored = Assert.Single(await CallbackRequestTestKit.ReadCallbackRequestsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(blankCookie)));
        Assert.Null(blankStored.Note);
        var trimmedStored = Assert.Single(await CallbackRequestTestKit.ReadCallbackRequestsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(trimmedCookie)));
        Assert.Equal("Gọi sau 18h", trimmedStored.Note);
    }

    [Fact]
    // Hội thoại `closed` vẫn nhận yêu cầu gọi lại. Hợp đồng không có lỗi cho trường hợp này.
    public async Task Post_ClosedConversation_StillAcceptsTheRequest()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie, status: SupportConversationStatus.Closed);

        var response = await PostAsync(cookie, new { conversationId = conversation.Id, phone = "0901234567" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    // Response 201 thật phải khớp tập khóa và kiểu của ví dụ gọi lại trong hợp đồng phía khách.
    public async Task Post_ValidRequest_MatchesTheContractExample()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        var response = await PostAsync(
            cookie,
            new { conversationId = conversation.Id, phone = "0901234567", note = "Gọi sau 18 giờ giúp mình." });

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body, response.StatusCode, "customer-api.json", "khách để lại số điện thoại");
    }

    private async Task<SupportConversation> SeedConversationAsync(
        string cookie, string? customerUserId = null, SupportConversationStatus status = SupportConversationStatus.Open)
    {
        return await SupportConversationTestKit.SeedConversationAsync(
            database,
            SupportConversationTestKit.ReadAnonymousId(cookie),
            customerUserId: customerUserId,
            status: status);
    }

    private Task<HttpResponseMessage> PostAsync(string cookie, object body)
    {
        return _client.SendAsync(SupportConversationTestKit.Post(CallbackRequestsPath, cookie, body));
    }

    private Task<HttpResponseMessage> PostRawAsync(string cookie, string json)
    {
        return _client.SendAsync(SupportConversationTestKit.PostRaw(CallbackRequestsPath, cookie, json));
    }

    // Đòi lỗi 400 `VALIDATION_ERROR` và khóa đúng trường sai. Test không khóa câu thông báo, chỉ khóa tên trường.
    private static async Task AssertValidationKeyAsync(HttpResponseMessage response, string expectedKey)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var error = document!.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var detail = Assert.Single(error.GetProperty("details").EnumerateArray());
        Assert.Equal(expectedKey, detail.GetProperty("key").GetString());
    }
}
