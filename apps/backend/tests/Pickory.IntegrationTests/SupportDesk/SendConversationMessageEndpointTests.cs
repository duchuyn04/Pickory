using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-15: `POST /api/support/conversations/{id}/messages`. Host riêng có đồng hồ giả, nên giới hạn tin kiểm được mà không chờ thật.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class SendConversationMessageEndpointTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string RateLimitedCode = "SUPPORT_MESSAGE_RATE_LIMITED";

    private readonly FakeTimeProvider _clock = new(SupportDeskTestKit.NowAtMillisecondPrecision());
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
    }

    [Fact]
    public async Task Post_ValidMessage_Returns201StoresTrimmedBodyAndUpdatesLastMessageAt()
    {
        var (cookie, conversation) = await SeedGuestConversationAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));

        var response = await SendAsync(conversation.Id, cookie, new { body = "  Cho mình hỏi về đơn hàng \n" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(["id", "role", "body", "action", "createdAt"], data.EnumerateObject().Select(property => property.Name));
        Assert.Equal("customer", data.GetProperty("role").GetString());
        Assert.Equal("Cho mình hỏi về đơn hàng", data.GetProperty("body").GetString());

        var stored = Assert.Single(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
        Assert.Equal(data.GetProperty("id").GetString(), stored.Id);
        Assert.Equal(SupportMessageRole.Customer, stored.Role);
        Assert.Equal("Cho mình hỏi về đơn hàng", stored.Body);
        // Giờ tin và `LastMessageAt` cùng lấy từ đồng hồ của host, và `LastMessageAt` đã dời khỏi giờ seed.
        var expectedTime = _clock.GetUtcNow().UtcDateTime;
        Assert.Equal(expectedTime, stored.CreatedAt);
        var conversationAfter = await SupportConversationTestKit.ReadConversationAsync(database, conversation.Id);
        Assert.Equal(expectedTime, conversationAfter.LastMessageAt);
        Assert.True(conversationAfter.LastMessageAt > conversation.LastMessageAt);
    }

    [Fact]
    public async Task Post_LoggedInCustomer_CanSendToOwnConversation()
    {
        var (user, cookie) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie.Split("; ")[1]), customerUserId: user.Id);

        var response = await SendAsync(conversation.Id, cookie, new { body = "Xin chào" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_BodyOfExactly2000Characters_Returns201()
    {
        var (cookie, conversation) = await SeedGuestConversationAsync();

        var response = await SendAsync(conversation.Id, cookie, new { body = new string('a', 2000) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_BodyOf2001Characters_Returns400AndStoresNothing()
    {
        var (cookie, conversation) = await SeedGuestConversationAsync();

        var response = await SendAsync(conversation.Id, cookie, new { body = new string('a', 2001) });

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Empty(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"body":null}""")]
    [InlineData("""{"body":""}""")]
    [InlineData("""{"body":"   "}""")]
    [InlineData("""{"body":42}""")]
    [InlineData("""{"body":["a"]}""")]
    [InlineData("""null""")]
    public async Task Post_MissingEmptyOrWrongTypeBody_Returns400AndStoresNothing(string json)
    {
        var (cookie, conversation) = await SeedGuestConversationAsync();

        var response = await _client.SendAsync(SupportConversationTestKit.PostRaw(
            SupportConversationTestKit.MessagesPath(conversation.Id), cookie, json));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Empty(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
    }

    [Fact]
    public async Task Post_ToConversationOfAnotherGuest_Returns404AndStoresNothing()
    {
        var (_, conversation) = await SeedGuestConversationAsync();

        var response = await SendAsync(conversation.Id, SupportConversationTestKit.NewGuestCookie(), new { body = "Xin chào" });

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        Assert.Empty(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
    }

    [Fact]
    public async Task Post_ToConversationOfAnotherCustomerAccount_Returns404AndStoresNothing()
    {
        var (owner, _) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString(), customerUserId: owner.Id);
        var (_, otherCookie) = await SupportConversationTestKit.CreateCustomerAsync(database);

        var response = await SendAsync(conversation.Id, otherCookie, new { body = "Xin chào" });

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        Assert.Empty(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
    }

    [Fact]
    public async Task Post_GuestWithSameAnonymousIdAsAccountConversation_Returns404AndStoresNothing()
    {
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var accountConversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(guestCookie), customerUserId: Guid.NewGuid().ToString());

        var response = await SendAsync(accountConversation.Id, guestCookie, new { body = "Xin chào" });

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        Assert.Empty(await SupportConversationTestKit.ReadMessagesAsync(database, accountConversation.Id));
    }

    [Fact]
    public async Task Post_ToClosedConversation_Returns409AndStoresNothing()
    {
        var (cookie, conversation) = await SeedGuestConversationAsync(SupportConversationStatus.Closed);

        var response = await SendAsync(conversation.Id, cookie, new { body = "Xin chào" });

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.Conflict, "SUPPORT_CONVERSATION_CLOSED");
        Assert.Empty(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
    }

    [Fact]
    public async Task Post_Staff_Returns403EvenWithInvalidBody()
    {
        var (_, conversation) = await SeedGuestConversationAsync();
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);

        var response = await SendAsync(conversation.Id, staffCookie, new { body = "" });

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Empty(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
    }

    [Fact]
    public async Task Post_EleventhMessageWithinOneMinute_Returns429AndIsNotStored_ThenWorksAfterOneMinute()
    {
        var (cookie, conversation) = await SeedGuestConversationAsync();
        for (var index = 1; index <= 10; index++)
        {
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(conversation.Id, cookie, new { body = $"Tin {index}" })).StatusCode);
        }

        var blocked = await SendAsync(conversation.Id, cookie, new { body = "Tin bị chặn" });

        await SupportConversationTestKit.AssertErrorAsync(blocked, HttpStatusCode.TooManyRequests, RateLimitedCode);
        var storedBodies = (await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id)).Select(message => message.Body).ToList();
        Assert.Equal(10, storedBodies.Count);
        Assert.DoesNotContain("Tin bị chặn", storedBodies);

        // Qua hết cửa sổ một phút thì gửi lại được.
        _clock.Advance(TimeSpan.FromSeconds(61));
        var afterWindow = await SendAsync(conversation.Id, cookie, new { body = "Tin sau một phút" });
        Assert.Equal(HttpStatusCode.Created, afterWindow.StatusCode);
    }

    [Fact]
    public async Task Post_LimitIsPerAnonymousIdAndSharedByEveryConversationOfThatBrowser()
    {
        var (cookie, conversation) = await SeedGuestConversationAsync();
        for (var index = 1; index <= 10; index++)
        {
            await SendAsync(conversation.Id, cookie, new { body = $"Tin {index}" });
        }

        // Khách khác có `pk_anon` khác nên có bộ đếm riêng, không bị ảnh hưởng.
        var (otherCookie, otherConversation) = await SeedGuestConversationAsync();
        var otherResponse = await SendAsync(otherConversation.Id, otherCookie, new { body = "Xin chào" });

        Assert.Equal(HttpStatusCode.Created, otherResponse.StatusCode);
    }

    [Fact]
    public async Task Post_RequestsRejectedBeforeSaving_DoNotCountTowardsLimit()
    {
        var (cookie, conversation) = await SeedGuestConversationAsync();
        var (_, closedConversation) = await SeedGuestConversationAsync(SupportConversationStatus.Closed, cookie);
        var (foreignCookie, _) = await SeedGuestConversationAsync();

        // Mỗi loại lỗi gửi nhiều hơn 10 lần. Nếu lỗi nào cũng tính lượt thì phần gửi hợp lệ phía dưới đã bị chặn.
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await SendAsync(conversation.Id, cookie, new { body = "   " });
            await SendAsync(conversation.Id, foreignCookie, new { body = "Xin chào" });
            await SendAsync(closedConversation.Id, cookie, new { body = "Xin chào" });
        }

        for (var index = 1; index <= 10; index++)
        {
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(conversation.Id, cookie, new { body = $"Tin {index}" })).StatusCode);
        }
    }

    [Fact]
    public async Task Post_201stMessageWithinOneDay_Returns429EvenWhenMinuteLimitIsNotHit()
    {
        var (cookie, conversation) = await SeedGuestConversationAsync();
        // Cách nhau 7 giây nên mỗi phút tối đa 9 tin, luôn dưới giới hạn phút. Tổng 200 tin trải trong khoảng 23 phút.
        for (var index = 1; index <= 200; index++)
        {
            var response = await SendAsync(conversation.Id, cookie, new { body = $"Tin {index}" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            _clock.Advance(TimeSpan.FromSeconds(7));
        }

        var blocked = await SendAsync(conversation.Id, cookie, new { body = "Tin thứ 201" });

        await SupportConversationTestKit.AssertErrorAsync(blocked, HttpStatusCode.TooManyRequests, RateLimitedCode);
        Assert.Equal(200, (await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id)).Count);

        // Cửa sổ 24 giờ tính từ lượt đầu. Qua 24 giờ thì gửi lại được.
        _clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(conversation.Id, cookie, new { body = "Ngày mới" })).StatusCode);
    }

    private async Task<(string Cookie, Modules.SupportDesk.Entities.SupportConversation Conversation)> SeedGuestConversationAsync(
        SupportConversationStatus status = SupportConversationStatus.Open, string? existingCookie = null)
    {
        var cookie = existingCookie ?? SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie), status: status);
        return (cookie, conversation);
    }

    private Task<HttpResponseMessage> SendAsync(string conversationId, string cookie, object body)
    {
        return _client.SendAsync(SupportConversationTestKit.Post(SupportConversationTestKit.MessagesPath(conversationId), cookie, body));
    }
}
