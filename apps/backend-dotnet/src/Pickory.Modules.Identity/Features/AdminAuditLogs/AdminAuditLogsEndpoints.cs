using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminAuditLogs;

// `GET /api/admin/audit-logs` chỉ đọc. Module cố ý không có route sửa hoặc xóa nhật ký.
public static class AdminAuditLogsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/audit-logs", ListAsync).RequirePermission(Permissions.AuditLogsRead);
    }

    private static async Task<IResult> ListAsync(HttpContext context, AuditLogReadHandler handler)
    {
        // Kiểm tra bộ lọc trước khi truy vấn. Giá trị sai nhận lỗi `VALIDATION_ERROR`.
        var query = AuditLogQuery.Read(context.Request.Query);
        var result = await handler.ListAsync(query, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<AuditLogItemResponse>>(result));
    }
}
