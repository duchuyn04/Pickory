using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra endpoint GET /api/admin/support/conversations/{id}/messages theo tiêu chí SD-07.
[Collection(SupportDeskCollection.Name)]
public sealed class AdminSupportConversationMessagesEndpointTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 2, 20, 0, TimeSpan.Zero);

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
    }

    private static string MessagesPath(string conversationId, string? query = null) =>
        $"/api/admin/support/conversations/{conversationId}/messages{(query is not null ? $"?{query}" : "")}";

    [Fact]
    // Nhân viên không có quyền conversations:read phải nhận 403 Forbidden.
    public async Task Get_SalesWithoutReadPermission_Returns403()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SalesCode, "list-sales");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Get, MessagesPath("dummy-id"), body: null, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Lấy tin của hội thoại không tồn tại phải trả 404 SUPPORT_CONVERSATION_NOT_FOUND.
    public async Task Get_UnknownConversation_Returns404ConversationNotFound()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "list-404");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Get, MessagesPath(Guid.NewGuid().ToString()), body: null, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
    }

    [Fact]
    // Danh sách trả về mỗi tin có sessionId và staffDisplayName đúng vai trò.
    public async Task Get_ValidConversation_ReturnsMessagesWithSessionIdAndDisplayName()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "list-ok");

        // Đặt tên hồ sơ cho nhân viên trong bảng Users.
        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = "Lan - CSKH";
            await identity.SaveChangesAsync();
        }

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString());

        var session = new SupportSession
        {
            ConversationId = conversation.Id,
            HandoffAt = TestMoment.UtcDateTime.AddMinutes(-10),
            HandoffInHours = true,
            AnonymousId = conversation.AnonymousId,
            StaffId = staff.Id,
            ClaimedAt = TestMoment.UtcDateTime.AddMinutes(-5),
        };

        await using (var context = database.CreateSupportDeskContext())
        {
            context.Sessions.Add(session);
            // Tin của khách trong phiên.
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.Customer,
                Body = "Đơn PK123 của mình giao tới đâu rồi?",
                SessionId = session.Id,
                CreatedAt = TestMoment.UtcDateTime.AddMinutes(-5),
            });
            // Tin của nhân viên trong phiên.
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.Staff,
                StaffId = staff.Id,
                Body = "Chào bạn, mình kiểm đơn PK123 ngay nhé.",
                SessionId = session.Id,
                CreatedAt = TestMoment.UtcDateTime.AddMinutes(-2),
            });
            await context.SaveChangesAsync();
        }

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Get, MessagesPath(conversation.Id), body: null, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        var data = root!.Data;
        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        // Tin khách: sessionId có giá trị, staffDisplayName bằng null.
        Assert.Equal("customer", items[0].GetProperty("role").GetString());
        Assert.Equal(session.Id, items[0].GetProperty("sessionId").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("staffDisplayName").ValueKind);

        // Tin nhân viên: sessionId có giá trị, staffDisplayName là tên hồ sơ.
        Assert.Equal("staff", items[1].GetProperty("role").GetString());
        Assert.Equal(session.Id, items[1].GetProperty("sessionId").GetString());
        Assert.Equal("Lan - CSKH", items[1].GetProperty("staffDisplayName").GetString());
    }

    [Fact]
    // Tin không thuộc phiên nào thì sessionId trả về null.
    public async Task Get_MessageWithoutSession_ReturnsNullSessionId()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "nosess-staff");
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString());

        await using (var context = database.CreateSupportDeskContext())
        {
            // Tin tạo khi chưa có phiên nào.
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.Customer,
                Body = "Tin nhắn ngoài phiên",
                SessionId = null,
                CreatedAt = TestMoment.UtcDateTime.AddMinutes(-1),
            });
            await context.SaveChangesAsync();
        }

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Get, MessagesPath(conversation.Id), body: null, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        var items = root!.Data.GetProperty("items").EnumerateArray().ToList();
        var messageItem = Assert.Single(items);
        Assert.Equal(JsonValueKind.Null, messageItem.GetProperty("sessionId").ValueKind);
    }

    [Fact]
    // Phân trang bằng con trỏ trả về nextCursor và đọc trang cũ hơn chính xác.
    public async Task Get_CursorPagination_ReturnsOlderPageCorrectly()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "page-staff");
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString());

        await using (var context = database.CreateSupportDeskContext())
        {
            for (var index = 0; index < 3; index++)
            {
                context.Messages.Add(new SupportMessage
                {
                    ConversationId = conversation.Id,
                    Role = SupportMessageRole.Customer,
                    Body = $"Tin số {index}",
                    CreatedAt = TestMoment.UtcDateTime.AddMinutes(index),
                });
            }
            await context.SaveChangesAsync();
        }

        // Lấy trang đầu với giới hạn 2 tin.
        var requestPage1 = ActorHttp.CreateJsonRequest(
            HttpMethod.Get, MessagesPath(conversation.Id, "limit=2"), body: null, $"pk_session={token}");
        var responsePage1 = await _client.SendAsync(requestPage1);
        Assert.Equal(HttpStatusCode.OK, responsePage1.StatusCode);

        var data1 = (await responsePage1.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        var items1 = data1.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items1.Count);
        var nextCursor = data1.GetProperty("nextCursor").GetString();
        Assert.NotNull(nextCursor);

        // Lấy trang tiếp theo dùng con trỏ vừa nhận.
        var requestPage2 = ActorHttp.CreateJsonRequest(
            HttpMethod.Get, MessagesPath(conversation.Id, $"cursor={nextCursor}&limit=2"), body: null, $"pk_session={token}");
        var responsePage2 = await _client.SendAsync(requestPage2);
        Assert.Equal(HttpStatusCode.OK, responsePage2.StatusCode);

        var data2 = (await responsePage2.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        var items2 = data2.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items2);
        Assert.Equal(JsonValueKind.Null, data2.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    // Tên hồ sơ nhân viên chứa ký tự @ hợp lệ vẫn trả đúng staffDisplayName trong lịch sử.
    public async Task Get_ValidConversationWithAtSignStaffName_ReturnsDisplayNameWithAtSign()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "hist-at");

        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = "Lan @ CSKH";
            await identity.SaveChangesAsync();
        }

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString());

        await using (var context = database.CreateSupportDeskContext())
        {
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.Staff,
                StaffId = staff.Id,
                Body = "Chào bạn.",
                CreatedAt = TestMoment.UtcDateTime.AddMinutes(-1),
            });
            await context.SaveChangesAsync();
        }

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Get, MessagesPath(conversation.Id), body: null, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        var items = root!.Data.GetProperty("items").EnumerateArray().ToList();
        var staffItem = Assert.Single(items);
        Assert.Equal("Lan @ CSKH", staffItem.GetProperty("staffDisplayName").GetString());
    }
}
