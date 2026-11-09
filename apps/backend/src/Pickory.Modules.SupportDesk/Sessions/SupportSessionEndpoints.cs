using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

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
        // Chuyển phiên hỗ trợ cho đồng nghiệp cần quyền trả lời hội thoại.
        endpoints.MapPut($"{SessionsPath}/{{id}}/assignment", TransferAsync)
            .RequirePermission(Permissions.ConversationsReply);
        // Kết thúc phiên hỗ trợ CSKH cần quyền trả lời hội thoại.
        endpoints.MapPatch($"{SessionsPath}/{{id}}", CloseAsync)
            .RequirePermission(Permissions.ConversationsReply);
        // Nhân viên gửi tin nhắn vào phiên hỗ trợ cần quyền trả lời hội thoại.
        endpoints.MapPost($"{SessionsPath}/{{id}}/messages", SendMessageAsync)
            .RequirePermission(Permissions.ConversationsReply);
        // Đọc thông tin khách hàng và đơn hàng trong phiên hỗ trợ cần quyền đọc hội thoại.
        endpoints.MapGet($"{SessionsPath}/{{id}}/customer", GetCustomerPanelAsync)
            .RequirePermission(Permissions.ConversationsRead);
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

    // Nhân viên gửi tin nhắn vai staff vào phiên đang nhận.
    internal static async Task<IResult> SendMessageAsync(
        string id,
        HttpContext context,
        SendMessageRequest? request,
        IValidator<SendMessageRequest> validator,
        SupportSessionMessageService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        var staffId = SupportStaff.RequireStaffId(context);
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var message = await service.SendMessageAsync(id, staffId, validRequest.TrimmedBody, context.RequestAborted);
        return Results.Json(
            new ApiResponse<StaffViewMessageResponse>(message),
            statusCode: StatusCodes.Status201Created);
    }

    // Nhân viên chuyển phiên đang nhận cho một đồng nghiệp đang sẵn sàng.
    internal static async Task<IResult> TransferAsync(
        string id,
        HttpContext context,
        TransferSessionRequest? request,
        IValidator<TransferSessionRequest> validator,
        SupportSessionTransferService service)
    {
        var callerStaffId = SupportStaff.RequireStaffId(context);
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // Không cho phép tự chuyển phiên cho chính người đang gọi.
        if (validRequest.TrimmedStaffId == callerStaffId)
        {
            throw new ValidationException(
            [
                new ValidationFailure("staffId", "Không thể tự chuyển phiên cho chính mình.")
            ]);
        }

        var transferred = await service.TransferAsync(
            id,
            callerStaffId,
            validRequest.TrimmedStaffId,
            validRequest.TrimmedNote,
            context.RequestAborted);
        return Results.Ok(new ApiResponse<SupportSessionClaimResponse>(transferred));
    }

    // Nhân viên kết thúc phiên hỗ trợ đang phụ trách và gửi tin mời chấm điểm CSAT.
    internal static async Task<IResult> CloseAsync(
        string id,
        HttpContext context,
        CloseSessionRequest? request,
        IValidator<CloseSessionRequest> validator,
        SupportSessionCloseService service)
    {
        var callerStaffId = SupportStaff.RequireStaffId(context);
        await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var closed = await service.CloseAsync(id, callerStaffId, context.RequestAborted);
        return Results.Ok(new ApiResponse<SupportSessionCloseResponse>(closed));
    }

    // Đọc bảng thông tin khách hàng cho cột phải màn hình hỗ trợ CSKH.
    internal static async Task<IResult> GetCustomerPanelAsync(
        string id,
        HttpContext context,
        SupportSessionCustomerService service)
    {
        // Phản hồi có thông tin khách hàng nên chặn lưu bộ nhớ đệm trên proxy hoặc trình duyệt.
        context.Response.Headers.CacheControl = "no-store";
        var result = await service.GetCustomerPanelAsync(id, context.RequestAborted);
        return Results.Ok(new ApiResponse<SupportSessionCustomerPanelResponse>(result));
    }
}
