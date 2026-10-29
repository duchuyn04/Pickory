using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.SupportDesk.Presence;

namespace Pickory.Modules.SupportDesk.Realtime;

// Luồng sự kiện SSE cho nhân viên CSKH. Luồng không tự kết thúc, client đóng kết nối khi rời màn hình.
internal static class SupportEventsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Quyền được kiểm trước khi mở luồng. Người thiếu quyền nhận 403 và không đăng ký vào hub.
        endpoints.MapGet("/api/admin/support/events", OpenEventStream)
            .RequirePermission(Permissions.ConversationsRead)
            // Khai báo kiểu nội dung để tài liệu OpenAPI mô tả đúng luồng SSE. Hàm trả `IResult` nên OpenAPI không tự suy ra kiểu nội dung.
            .Produces<string>(StatusCodes.Status200OK, "text/event-stream");
    }

    internal static IResult OpenEventStream(HttpContext context, SupportEventStreamer streamer)
    {
        var staffId = SupportStaff.RequireStaffId(context);
        return new SupportEventStreamResult(streamer.StreamAsync(staffId, context.RequestAborted));
    }
}
