using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-15: `GET /api/support/conversations/{id}/messages`. Chỉ chủ hội thoại đọc được lịch sử, và nhân viên không dùng route này.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ListConversationMessagesEndpointTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string NotFoundCode = "SUPPORT_CONVERSATION_NOT_FOUND";
    private static readonly DateTime BaseTime = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => SupportConversationTestKit.DeleteIssuedConversationsAsync(database);

    [Fact]
    public async Task Get_OwnerGuest_ReturnsMessagesAscendingWithPublicFieldsOnly()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, "Chào bạn", BaseTime.AddMinutes(2), SupportMessageRole.Staff);
        await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, "Xin chào", BaseTime.AddMinutes(1));
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversation.Id, "Để lại số điện thoại", BaseTime.AddMinutes(3), SupportMessageRole.System, action: "callback_form");

        var response = await _client.SendAsync(SupportConversationTestKit.Get(SupportConversationTestKit.MessagesPath(conversation.Id), cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(["items", "nextCursor"], data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("nextCursor").ValueKind);
        var items = data.GetProperty("items").EnumerateArray().ToList();
        // Thứ tự theo giờ tạo tăng dần, dù thứ tự seed khác. Tin mới nhất nằm cuối.
        Assert.Equal(["Xin chào", "Chào bạn", "Để lại số điện thoại"], items.Select(item => item.GetProperty("body").GetString()));
        Assert.Equal(["customer", "staff", "system"], items.Select(item => item.GetProperty("role").GetString()));
        Assert.Equal("callback_form", items[2].GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("action").ValueKind);
        // Không lộ `staffId`: tên nhân viên chưa được chốt cho khách thấy.
        Assert.Equal(["id", "role", "body", "action", "createdAt"], items[0].EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Get_ConversationWithoutMessages_ReturnsEmptyPage()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));

        var response = await _client.SendAsync(SupportConversationTestKit.Get(SupportConversationTestKit.MessagesPath(conversation.Id), cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Get_ClosedConversation_StillReturnsHistory()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie), status: SupportConversationStatus.Closed);
        await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, "Tin cũ", BaseTime);

        var response = await _client.SendAsync(SupportConversationTestKit.Get(SupportConversationTestKit.MessagesPath(conversation.Id), cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal("Tin cũ", Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("body").GetString());
    }

    [Fact]
    public async Task Get_OwnerCustomer_ReadsConversationOwnedByAccount()
    {
        var (user, cookie) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId: Guid.NewGuid().ToString(), customerUserId: user.Id);
        await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, "Từ máy khác", BaseTime);

        var response = await _client.SendAsync(SupportConversationTestKit.Get(SupportConversationTestKit.MessagesPath(conversation.Id), cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_OtherGuestWithDifferentAnonymousId_Returns404()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(ownerCookie));
        await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, "Riêng tư", BaseTime);

        var response = await _client.SendAsync(SupportConversationTestKit.Get(
            SupportConversationTestKit.MessagesPath(conversation.Id), SupportConversationTestKit.NewGuestCookie()));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, NotFoundCode);
    }

    [Fact]
    public async Task Get_OtherCustomerAccount_Returns404()
    {
        var (owner, _) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString(), customerUserId: owner.Id);
        var (_, otherCookie) = await SupportConversationTestKit.CreateCustomerAsync(database);

        var response = await _client.SendAsync(SupportConversationTestKit.Get(SupportConversationTestKit.MessagesPath(conversation.Id), otherCookie));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, NotFoundCode);
    }

    [Fact]
    public async Task Get_GuestWithSameAnonymousIdAsAccountConversation_Returns404()
    {
        // Chủ tài khoản đã đăng xuất. Người dùng chung trình duyệt giữ cùng `pk_anon` nhưng không phải chủ.
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var accountConversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(guestCookie), customerUserId: Guid.NewGuid().ToString());
        await SupportConversationTestKit.SeedMessageAsync(database, accountConversation.Id, "Của chủ tài khoản", BaseTime);

        var response = await _client.SendAsync(SupportConversationTestKit.Get(
            SupportConversationTestKit.MessagesPath(accountConversation.Id), guestCookie));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, NotFoundCode);
    }

    [Fact]
    public async Task Get_UnknownConversationId_Returns404WithSameCodeAsForeignConversation()
    {
        var response = await _client.SendAsync(SupportConversationTestKit.Get(
            SupportConversationTestKit.MessagesPath(Guid.NewGuid().ToString()), SupportConversationTestKit.NewGuestCookie()));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, NotFoundCode);
    }

    [Fact]
    public async Task Get_Staff_Returns403()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);

        var response = await _client.SendAsync(SupportConversationTestKit.Get(SupportConversationTestKit.MessagesPath(conversation.Id), staffCookie));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Get_FiveMessagesWithLimitTwo_ReturnsThreePagesFromNewestToOldest()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        for (var index = 1; index <= 5; index++)
        {
            await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, $"Tin {index}", BaseTime.AddMinutes(index));
        }

        var path = SupportConversationTestKit.MessagesPath(conversation.Id);
        var firstPage = await SupportConversationTestKit.ReadDataAsync(
            await _client.SendAsync(SupportConversationTestKit.Get($"{path}?limit=2", cookie)));
        var secondPage = await SupportConversationTestKit.ReadDataAsync(await _client.SendAsync(SupportConversationTestKit.Get(
            $"{path}?limit=2&cursor={Uri.EscapeDataString(firstPage.GetProperty("nextCursor").GetString()!)}", cookie)));
        var thirdPage = await SupportConversationTestKit.ReadDataAsync(await _client.SendAsync(SupportConversationTestKit.Get(
            $"{path}?limit=2&cursor={Uri.EscapeDataString(secondPage.GetProperty("nextCursor").GetString()!)}", cookie)));

        // Trang đầu có hai tin mới nhất, trong trang tin cũ đứng trước.
        Assert.Equal(["Tin 4", "Tin 5"], ReadBodies(firstPage));
        Assert.Equal(["Tin 2", "Tin 3"], ReadBodies(secondPage));
        Assert.Equal(["Tin 1"], ReadBodies(thirdPage));
        Assert.Equal(JsonValueKind.Null, thirdPage.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Get_MessagesWithSameCreatedAt_PagesWithoutLossOrDuplicate()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        // Bốn tin cùng một giờ. Chỉ `Id` phân biệt được, nên con trỏ phải dùng cả `Id`.
        for (var index = 1; index <= 4; index++)
        {
            await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, $"Tin {index}", BaseTime);
        }

        var path = SupportConversationTestKit.MessagesPath(conversation.Id);
        var seenBodies = new List<string>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            var url = cursor is null ? $"{path}?limit=3" : $"{path}?limit=3&cursor={Uri.EscapeDataString(cursor)}";
            var page = await SupportConversationTestKit.ReadDataAsync(await _client.SendAsync(SupportConversationTestKit.Get(url, cookie)));
            seenBodies.AddRange(ReadBodies(page));
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
            pageCount++;
        }
        while (cursor is not null && pageCount < 5);

        Assert.Equal(2, pageCount);
        Assert.Equal(["Tin 1", "Tin 2", "Tin 3", "Tin 4"], seenBodies.Order());
        Assert.Equal(4, seenBodies.Distinct().Count());
    }

    [Fact]
    public async Task Get_ExactlyLimitMessages_HasNoNextCursor()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, "Tin 1", BaseTime);
        await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, "Tin 2", BaseTime.AddMinutes(1));

        var response = await _client.SendAsync(SupportConversationTestKit.Get(
            $"{SupportConversationTestKit.MessagesPath(conversation.Id)}?limit=2", cookie));

        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Get_DefaultLimit_ReturnsFiftyNewestMessages()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SeedManyMessagesAsync(conversation.Id, 51);

        var response = await _client.SendAsync(SupportConversationTestKit.Get(SupportConversationTestKit.MessagesPath(conversation.Id), cookie));

        var data = await SupportConversationTestKit.ReadDataAsync(response);
        var bodies = ReadBodies(data);
        Assert.Equal(50, bodies.Count);
        // 51 tin, trang đầu bỏ tin cũ nhất (số 1).
        Assert.Equal("Tin 2", bodies[0]);
        Assert.Equal("Tin 51", bodies[^1]);
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Get_LimitOfOneHundred_IsAccepted()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));

        var response = await _client.SendAsync(SupportConversationTestKit.Get(
            $"{SupportConversationTestKit.MessagesPath(conversation.Id)}?limit=100", cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("limit=abc")]
    [InlineData("limit=2.5")]
    [InlineData("cursor=khong-phai-cursor")]
    [InlineData("cursor=")]
    public async Task Get_InvalidLimitOrCursor_Returns400(string query)
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));

        var response = await _client.SendAsync(SupportConversationTestKit.Get(
            $"{SupportConversationTestKit.MessagesPath(conversation.Id)}?{query}", cookie));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Get_History_NeverContainsTransferNoteOfSession()
    {
        const string secretNote = "GHI-CHU-NOI-BO-KHONG-DUOC-LO";
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportConversationTestKit.SeedMessageAsync(database, conversation.Id, "Xin chào", BaseTime);
        await using (var context = database.CreateSupportDeskContext())
        {
            context.Sessions.Add(new SupportSession
            {
                ConversationId = conversation.Id,
                AnonymousId = conversation.AnonymousId,
                HandoffAt = BaseTime,
                TransferNote = secretNote,
            });
            await context.SaveChangesAsync();
        }

        var response = await _client.SendAsync(SupportConversationTestKit.Get(SupportConversationTestKit.MessagesPath(conversation.Id), cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(secretNote, await response.Content.ReadAsStringAsync());
    }

    private async Task SeedManyMessagesAsync(string conversationId, int count)
    {
        await using var context = database.CreateSupportDeskContext();
        for (var index = 1; index <= count; index++)
        {
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversationId,
                Role = SupportMessageRole.Customer,
                Body = $"Tin {index}",
                CreatedAt = BaseTime.AddMinutes(index),
            });
        }

        await context.SaveChangesAsync();
    }

    private static List<string> ReadBodies(JsonElement data) =>
        data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("body").GetString()!).ToList();
}
