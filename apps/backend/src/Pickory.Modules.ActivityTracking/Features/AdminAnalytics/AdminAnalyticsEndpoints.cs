using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.ActivityTracking.Contracts.Metrics;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.ActivityTracking.Features.AdminAnalytics;

// Số liệu hành vi cho nhân viên. Route chỉ trả số đếm gộp theo ngày, không trả dữ liệu theo từng khách.
internal static class AdminAnalyticsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Quyền `reports:read` được kiểm trước khi handler chạy, nên người thiếu quyền nhận 403 dù query sai.
        endpoints.MapGet("/api/admin/analytics/funnel", GetFunnelAsync)
            .RequirePermission(Permissions.ReportsRead);
    }

    internal static async Task<IResult> GetFunnelAsync(
        HttpContext context,
        IActivityMetrics metrics,
        CancellationToken cancellationToken)
    {
        // Kiểm toàn bộ query string trước khi chạm DB. Tham số sai không tốn một truy vấn.
        var failures = new List<ValidationFailure>();
        var range = FunnelRangeQuery.Read(context.Request.Query, failures);
        if (range is null)
        {
            throw new ValidationException(failures);
        }

        var funnel = await metrics.GetFunnelAsync(range, cancellationToken);
        return Results.Ok(new ApiResponse<FunnelResponse>(FunnelResponse.Create(range, funnel)));
    }
}
