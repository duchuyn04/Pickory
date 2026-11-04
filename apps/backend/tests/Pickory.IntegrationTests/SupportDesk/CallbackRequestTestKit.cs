using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.IntegrationTests.SupportDesk;

// Dụng cụ chung cho test SD-05: gieo và đọc lại bảng `callback_requests`, và dọn dữ liệu theo `anonymousId` đã cấp.
internal static class CallbackRequestTestKit
{
    public const string CallbackRequestsPath = "/api/support/callback-requests";
    public const string AdminCallbackRequestsPath = "/api/admin/support/callback-requests";

    // Mã `anonymousId` mà test đã dùng. Test dọn yêu cầu gọi lại theo danh sách này khi kết thúc.
    private static readonly ConcurrentBag<string> IssuedAnonymousIds = [];

    public static string AdminCallbackRequestPath(string callbackRequestId) =>
        $"{AdminCallbackRequestsPath}/{callbackRequestId}";

    public static void TrackAnonymousId(string anonymousId) => IssuedAnonymousIds.Add(anonymousId);

    // Gieo một yêu cầu gọi lại. Cột định danh lấy theo tham số để test dựng đúng dòng nó cần.
    public static async Task<CallbackRequest> SeedCallbackRequestAsync(
        IntegrationDatabaseFixture database,
        string anonymousId,
        string? conversationId = null,
        string? customerUserId = null,
        string? phone = "0901234567",
        string? note = "Gọi sau 18 giờ.",
        CallbackRequestStatus status = CallbackRequestStatus.Pending,
        DateTime? createdAt = null,
        string? handledBy = null,
        DateTime? handledAt = null)
    {
        IssuedAnonymousIds.Add(anonymousId);
        var request = new CallbackRequest
        {
            AnonymousId = anonymousId,
            ConversationId = conversationId,
            CustomerUserId = customerUserId,
            Phone = phone,
            Note = note,
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddHours(-1),
            HandledBy = handledBy,
            HandledAt = handledAt,
        };
        await using var context = database.CreateSupportDeskContext();
        context.CallbackRequests.Add(request);
        await context.SaveChangesAsync();
        return request;
    }

    public static async Task<IReadOnlyList<CallbackRequest>> ReadCallbackRequestsAsync(
        IntegrationDatabaseFixture database, string anonymousId)
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.CallbackRequests.AsNoTracking()
            .Where(request => request.AnonymousId == anonymousId)
            .OrderBy(request => request.CreatedAt)
            .ThenBy(request => request.Id)
            .ToListAsync();
    }

    public static async Task<CallbackRequest?> FindCallbackRequestAsync(
        IntegrationDatabaseFixture database, string callbackRequestId)
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.CallbackRequests.AsNoTracking()
            .SingleOrDefaultAsync(request => request.Id == callbackRequestId);
    }

    // Xóa mọi yêu cầu gọi lại của các `anonymousId` đã cấp. Các test dùng chung một DB nên mỗi test bắt đầu từ bảng sạch theo mã của nó.
    public static async Task DeleteIssuedCallbackRequestsAsync(IntegrationDatabaseFixture database)
    {
        var anonymousIds = IssuedAnonymousIds.ToArray();
        await using var context = database.CreateSupportDeskContext();
        await context.CallbackRequests
            .Where(request => anonymousIds.Contains(request.AnonymousId))
            .ExecuteDeleteAsync();
    }
}
