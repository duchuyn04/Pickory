using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.SupportDesk.Presence;

// Nhân viên bật hoặc tắt trạng thái sẵn sàng và xem ai đang sẵn sàng.
internal static class StaffPresenceEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Đổi trạng thái là hành động trả lời khách, nên cần `conversations:reply`. Người chỉ đọc không được tự bật.
        endpoints.MapPut("/api/admin/support/presence", SetPresenceAsync)
            .RequirePermission(Permissions.ConversationsReply);
        endpoints.MapGet("/api/admin/support/presence", ListAvailableAsync)
            .RequirePermission(Permissions.ConversationsReply);
    }

    internal static async Task<IResult> SetPresenceAsync(
        HttpContext context,
        SetPresenceRequest? request,
        IValidator<SetPresenceRequest> validator,
        StaffPresenceService presenceService)
    {
        // Kiểm body trước khi ghi DB. Giá trị lạ trả 400 và không đổi dòng trực nào.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // Validator đã xác nhận chuỗi hợp lệ. `TryParse` ở đây chỉ để lấy giá trị enum.
        StaffPresenceWire.TryParse(validRequest.StatusText, out var status);

        // Ghi cho chính người gọi. Nhân viên không đổi được trạng thái của người khác.
        var staffId = SupportStaff.RequireStaffId(context);
        var presence = await presenceService.SetStatusAsync(staffId, status, context.RequestAborted);
        return Results.Ok(new ApiResponse<StaffPresenceResponse>(presence));
    }

    internal static async Task<IResult> ListAvailableAsync(
        HttpContext context,
        StaffPresenceService presenceService)
    {
        var available = await presenceService.ListAvailableAsync(context.RequestAborted);
        return Results.Ok(new ApiResponse<IReadOnlyList<StaffPresenceResponse>>(available));
    }
}
