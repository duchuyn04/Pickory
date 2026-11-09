using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Route yêu cầu gọi lại phía khách và hai route quản trị trên tab Gọi lại của nhân viên.
internal static class CallbackRequestEndpoints
{
    private const string CallbackRequestsPath = "/api/support/callback-requests";
    private const string AdminCallbackRequestsPath = "/api/admin/support/callback-requests";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(CallbackRequestsPath, CreateAsync);
        // Tab Gọi lại chỉ đọc danh sách nên cần `conversations:read`.
        endpoints.MapGet(AdminCallbackRequestsPath, ListAsync)
            .RequirePermission(Permissions.ConversationsRead);
        // Đánh dấu kết quả cuộc gọi là thao tác phản hồi khách nên cần `conversations:reply`.
        endpoints.MapPatch($"{AdminCallbackRequestsPath}/{{id}}", UpdateStatusAsync)
            .RequirePermission(Permissions.ConversationsReply);
    }

    internal static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateCallbackRequest? request,
        IValidator<CreateCallbackRequest> validator,
        CallbackRequestService service)
    {
        // Số điện thoại là dữ liệu cá nhân, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";
        var actor = context.GetActor();
        // `RejectStaff` chạy trước validator, nên nhân viên gửi JSON hợp lệ nhưng sai luật vẫn nhận 403.
        ConversationOwnership.RejectStaff(actor);

        // Kiểm body trước khi chạm DB. Body sai không đọc hội thoại và không tính lượt giới hạn.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var created = await service.CreateAsync(
            actor,
            validRequest.ConversationIdText!,
            validRequest.NormalizedPhone,
            validRequest.TrimmedNote,
            context.RequestAborted);
        return Results.Json(
            new ApiResponse<CallbackRequestCreatedResponse>(created),
            statusCode: StatusCodes.Status201Created);
    }

    internal static async Task<IResult> ListAsync(
        HttpContext context,
        CallbackRequestService service)
    {
        var items = await service.ListAsync(context.RequestAborted);
        return Results.Ok(new ApiResponse<IReadOnlyList<CallbackRequestResponse>>(items));
    }

    internal static async Task<IResult> UpdateStatusAsync(
        string id,
        HttpContext context,
        UpdateCallbackRequest? request,
        IValidator<UpdateCallbackRequest> validator,
        CallbackRequestService service)
    {
        // Kiểm body trước khi đọc DB để giá trị lạ không chạm bảng `callback_requests`.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // Validator đã xác nhận chuỗi hợp lệ. `TryParseHandleStatus` lấy giá trị enum tương ứng.
        CallbackRequestWire.TryParseHandleStatus(validRequest.StatusText, out var status);

        // Ghi nhận chính nhân viên đang đăng nhập là người thực hiện cuộc gọi.
        var staffId = SupportStaff.RequireStaffId(context);
        var updated = await service.UpdateStatusAsync(id, staffId, status, context.RequestAborted);
        return Results.Ok(new ApiResponse<CallbackRequestResponse>(updated));
    }
}
