using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.AiGateway.Usage;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.AiGateway.Features.AdminUsage;

// `GET /api/admin/ai/usage` trả chi phí, số bước, tỉ lệ lỗi và độ trễ theo ngày và theo tính năng.
public static class AdminUsageEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/ai/usage", HandleAsync).RequirePermission(Permissions.AiUsageRead);
    }

    private static async Task<IResult> HandleAsync(HttpContext context, UsageSummaryReader reader, TimeProvider timeProvider)
    {
        // Kiểm tra khoảng thời gian trước khi truy vấn. Giá trị sai nhận lỗi 400, không thành lỗi DB.
        var query = UsageQuery.Read(context.Request.Query, timeProvider.GetUtcNow().UtcDateTime);
        var summary = await reader.ReadAsync(query.FromUtc, query.ToUtc, context.RequestAborted);
        return Results.Ok(new ApiResponse<UsageSummary>(summary));
    }
}
