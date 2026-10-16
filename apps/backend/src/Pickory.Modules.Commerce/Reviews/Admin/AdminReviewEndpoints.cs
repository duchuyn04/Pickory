using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Reviews.Admin;

// Hàng chờ kiểm duyệt và ẩn, hiện đánh giá của nhân viên. Cả hai route cần quyền `reviews:moderate`.
internal static class AdminReviewEndpoints
{
    private const string ReviewsPath = "/api/admin/reviews";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ReviewsPath, GetListAsync).RequirePermission(Permissions.ReviewsModerate);

        // Ẩn hoặc hiện là thay đổi một phần của đánh giá, nên dùng PATCH.
        endpoints.MapPatch($"{ReviewsPath}/{{id}}", UpdateAsync).RequirePermission(Permissions.ReviewsModerate);
    }

    private static async Task<IResult> GetListAsync(HttpContext context, AdminReviewModerationService service)
    {
        // Danh sách có nội dung do khách viết và tên người viết, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm toàn bộ query string trước khi chạm DB. Tham số sai không tốn một truy vấn.
        var failures = new List<ValidationFailure>();
        var filter = AdminReviewListQuery.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var result = await service.GetListAsync(filter, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<AdminReviewResponse>>(result));
    }

    private static async Task<IResult> UpdateAsync(
        string id, HttpContext context, JsonElement? body, AdminReviewModerationService service)
    {
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm body trước khi mở transaction. Body sai không khóa dòng và không ghi audit.
        var failures = new List<ValidationFailure>();
        var request = ReviewModerationRequest.Read(body, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Audit cần id thật.
        var actorId = context.GetActor().UserId!;
        var result = await service.SetHiddenAsync(id, request, actorId, context.RequestAborted);
        return Results.Ok(new ApiResponse<AdminReviewResponse>(result));
    }
}
