using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.IntegrationTests.SupportDesk;

// Dụng cụ chung cho test SD-15: cookie, dữ liệu seed và đọc lại DB cho hội thoại và tin của khách.
internal static class SupportConversationTestKit
{
    public const string ConversationsPath = "/api/support/conversations";

    public static string MessagesPath(string conversationId) => $"{ConversationsPath}/{conversationId}/messages";

    // Mã `pk_anon` mà test đã cấp. Test dọn hội thoại theo danh sách này khi kết thúc.
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> IssuedAnonymousIds = [];

    // Mỗi test dùng một `pk_anon` mới. Hai test không dùng chung bộ đếm giới hạn của host và không thấy hội thoại của nhau.
    public static string NewGuestCookie()
    {
        var anonymousId = Guid.NewGuid().ToString();
        IssuedAnonymousIds.Add(anonymousId);
        return $"pk_anon={anonymousId}";
    }

    // Xóa hội thoại của mọi `pk_anon` đã cấp, kèm tin và phiên. Test migration chạy Down tạo lại chỉ mục cũ theo `anonymousId`, và dữ liệu sót có hai hội thoại `open` cùng `anonymousId` làm Down lỗi.
    public static async Task DeleteIssuedConversationsAsync(IntegrationDatabaseFixture database)
    {
        var anonymousIds = IssuedAnonymousIds.ToArray();
        await using var context = database.CreateSupportDeskContext();
        await context.Conversations.Where(conversation => anonymousIds.Contains(conversation.AnonymousId)).ExecuteDeleteAsync();
    }

    public static string ReadAnonymousId(string cookie) => cookie["pk_anon=".Length..];

    // Tạo khách hàng đã đăng nhập. Cookie gồm phiên và một `pk_anon`, giống trình duyệt thật.
    public static async Task<(User User, string Cookie)> CreateCustomerAsync(
        IntegrationDatabaseFixture database, string? anonymousCookie = null)
    {
        var user = await IdentityTestData.CreateUserAsync(
            database, IdentityTestData.NewEmail("support-customer"), UserKind.Customer, "customer", emailVerified: true);
        var sessionToken = await IdentityTestData.CreateSessionAsync(database, user.Id);
        return (user, $"pk_session={sessionToken}; {anonymousCookie ?? NewGuestCookie()}");
    }

    public static async Task<string> CreateStaffCookieAsync(IntegrationDatabaseFixture database)
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(database, "support", "support-staff");
        return $"pk_session={token}";
    }

    public static HttpRequestMessage Post(string path, string cookie, object? body = null)
    {
        return ActorHttp.CreateJsonRequest(HttpMethod.Post, path, body, cookie);
    }

    // Gửi nguyên chuỗi JSON làm body. Test cần body sai kiểu mà đối tượng ẩn danh không tạo được.
    public static HttpRequestMessage PostRaw(string path, string cookie, string json)
    {
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Post, path, body: null, cookie);
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    public static HttpRequestMessage Get(string path, string cookie)
    {
        return ActorHttp.CreateRequest(HttpMethod.Get, path, cookie);
    }

    public static async Task<SupportConversation> SeedConversationAsync(
        IntegrationDatabaseFixture database,
        string anonymousId,
        string? customerUserId = null,
        SupportConversationStatus status = SupportConversationStatus.Open,
        DateTime? lastMessageAt = null)
    {
        IssuedAnonymousIds.Add(anonymousId);
        var createdAt = lastMessageAt ?? DateTime.UtcNow.AddHours(-1);
        var conversation = new SupportConversation
        {
            AnonymousId = anonymousId,
            CustomerUserId = customerUserId,
            Status = status,
            CreatedAt = createdAt,
            LastMessageAt = createdAt,
            ClosedAt = status == SupportConversationStatus.Closed ? createdAt : null,
        };
        await using var context = database.CreateSupportDeskContext();
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync();
        return conversation;
    }

    public static async Task<SupportMessage> SeedMessageAsync(
        IntegrationDatabaseFixture database,
        string conversationId,
        string body,
        DateTime createdAt,
        SupportMessageRole role = SupportMessageRole.Customer,
        string? action = null)
    {
        var message = new SupportMessage
        {
            ConversationId = conversationId,
            Role = role,
            Body = body,
            Action = action,
            CreatedAt = createdAt,
        };
        await using var context = database.CreateSupportDeskContext();
        context.Messages.Add(message);
        await context.SaveChangesAsync();
        return message;
    }

    public static async Task<IReadOnlyList<SupportConversation>> ReadConversationsAsync(
        IntegrationDatabaseFixture database, string anonymousId)
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.Conversations.AsNoTracking()
            .Where(conversation => conversation.AnonymousId == anonymousId)
            .OrderBy(conversation => conversation.CreatedAt)
            .ToListAsync();
    }

    public static async Task<SupportConversation> ReadConversationAsync(IntegrationDatabaseFixture database, string conversationId)
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.Conversations.AsNoTracking().SingleAsync(conversation => conversation.Id == conversationId);
    }

    public static async Task<IReadOnlyList<SupportMessage>> ReadMessagesAsync(
        IntegrationDatabaseFixture database, string conversationId)
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.Messages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
    }

    public static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return document!.RootElement.GetProperty("data").Clone();
    }

    public static async Task AssertErrorAsync(HttpResponseMessage response, System.Net.HttpStatusCode status, string code)
    {
        Xunit.Assert.Equal(status, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var error = document!.RootElement.GetProperty("error");
        Xunit.Assert.Equal(code, error.GetProperty("code").GetString());
        // Thông báo luôn có và viết cho người dùng. Test không khóa nội dung chữ, chỉ khóa mã lỗi.
        Xunit.Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }
}
