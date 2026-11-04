using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Shared.Http;

namespace Pickory.Modules.SupportDesk.Sessions;

// Tuyến đường quản trị phục vụ hàng chờ và nhận phiên hỗ trợ CSKH.
internal static class SupportSessionEndpoints
{
    private const string SessionsPath = "/api/admin/support/sessions";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Đọc danh sách hàng chờ và phiên cá nhân cần quyền đọc hội thoại.
        endpoints.MapGet(SessionsPath, ListQueueAsync)
            .RequirePermission(Permissions.ConversationsRead);
        // Nhận phiên hỗ trợ cần quyền trả lời hội thoại.
        endpoints.MapPost($"{SessionsPath}/{{id}}/assignment", ClaimAsync)
            .RequirePermission(Permissions.ConversationsReply);
    }

    internal static async Task<IResult> ListQueueAsync(
        HttpContext context,
        SupportSessionQueueService service)
    {
        var view = context.Request.Query["view"].ToString();
        // Tham số view là bắt buộc và chỉ chấp nhận hai giá trị waiting hoặc mine.
        if (string.IsNullOrWhiteSpace(view) || (view != "waiting" && view != "mine"))
        {
            throw new ValidationException(
            [
                new ValidationFailure("view", "Tham số 'view' là bắt buộc và chỉ nhận 'waiting' hoặc 'mine'.")
            ]);
        }

        // Lấy mã nhân viên đang đăng nhập để lọc đúng danh sách phiên cá nhân.
        var staffId = SupportStaff.RequireStaffId(context);
        var items = await service.ListAsync(view, staffId, context.RequestAborted);
        return Results.Ok(new ApiResponse<IReadOnlyList<SupportSessionQueueItemResponse>>(items));
    }

    internal static async Task<IResult> ClaimAsync(
        string id,
        HttpContext context,
        SupportSessionClaimService service)
    {
        // Lấy mã nhân viên đang đăng nhập để gán vào phiên hỗ trợ.
        var staffId = SupportStaff.RequireStaffId(context);
        var claimed = await service.ClaimAsync(id, staffId, context.RequestAborted);
        return Results.Json(
            new ApiResponse<SupportSessionClaimResponse>(claimed),
            statusCode: StatusCodes.Status201Created);
    }
}
