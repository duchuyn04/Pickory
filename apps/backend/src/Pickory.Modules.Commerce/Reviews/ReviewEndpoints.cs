using System.Globalization;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Reviews;

// Route đánh giá sản phẩm: danh sách công khai, còn xem, viết, sửa và báo cáo chỉ dành cho khách đã đăng nhập.
// Route ghi đi qua kiểm `Origin` của actor middleware, như mọi route ghi khác.
internal static class ReviewEndpoints
{
    private const string ReviewsPath = "/api/products/{productId}/reviews";
    private const string MinePath = "/api/products/{productId}/reviews/mine";
    private const string ReportsPath = "/api/reviews/{reviewId}/reports";
    private const string CustomerOnlyMessage = "Chức năng này chỉ dành cho tài khoản khách hàng.";
    private const string LimitMessage = "Số dòng mỗi trang phải là số nguyên từ 1 đến 50.";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Danh sách công khai, nên route không đòi phiên.
        endpoints.MapGet(ReviewsPath, ListAsync);

        // Chưa đăng nhập nhận 401 trước khi handler chạy. Loại tài khoản được kiểm trong handler.
        endpoints.MapGet(MinePath, GetMineAsync).RequireAuth();
        endpoints.MapPost(ReviewsPath, CreateAsync).RequireAuth();
        endpoints.MapPatch(MinePath, UpdateAsync).RequireAuth();
        endpoints.MapPost(ReportsPath, ReportAsync).RequireAuth();
    }

    private static async Task<IResult> ListAsync(string productId, HttpContext context, ReviewService service)
    {
        // Kiểm toàn bộ query string trước khi chạm DB. Tham số sai không tốn một truy vấn.
        var failures = new List<ValidationFailure>();
        var limit = ReadLimit(context.Request.Query, failures);
        var cursor = QueryParameter.ReadSingle(context.Request.Query, "cursor", failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var result = await service.ListAsync(productId, limit, cursor, context.RequestAborted);
        return Results.Ok(new ApiResponse<ProductReviewListResponse>(result));
    }

    private static async Task<IResult> GetMineAsync(string productId, HttpContext context, ReviewService service)
    {
        // Trạng thái viết đánh giá là dữ liệu cá nhân, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";
        var result = await service.GetMineAsync(productId, RequireCustomerId(context), context.RequestAborted);
        return Results.Ok(new ApiResponse<MyReviewStatusResponse>(result));
    }

    private static async Task<IResult> CreateAsync(
        string productId, HttpContext context, JsonElement? body, ReviewService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        var userId = RequireCustomerId(context);

        // Kiểm body trước mọi truy cập DB. Body sai không gọi catalog và không đọc đơn hàng.
        var input = ReviewRequests.ReadCreate(body);
        var created = await service.CreateAsync(productId, userId, input, context.RequestAborted);
        return Results.Json(new ApiResponse<OwnReviewResponse>(created), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateAsync(
        string productId, HttpContext context, JsonElement? body, ReviewService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        var userId = RequireCustomerId(context);

        var patch = ReviewRequests.ReadPatch(body);
        var updated = await service.UpdateAsync(productId, userId, patch, context.RequestAborted);
        return Results.Ok(new ApiResponse<OwnReviewResponse>(updated));
    }

    private static async Task<IResult> ReportAsync(string reviewId, HttpContext context, ReviewReportService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        var report = await service.ReportAsync(reviewId, RequireCustomerId(context), context.RequestAborted);
        return Results.Json(new ApiResponse<ReviewReportResponse>(report), statusCode: StatusCodes.Status201Created);
    }

    private static int ReadLimit(IQueryCollection query, List<ValidationFailure> failures)
    {
        var raw = QueryParameter.ReadSingle(query, "limit", failures);

        // Thiếu hoặc lặp tham số: dùng mặc định. Lỗi lặp đã được ghi vào `failures`.
        if (raw is null)
        {
            return ReviewService.DefaultLimit;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            || limit < 1 || limit > ReviewService.MaxLimit)
        {
            failures.Add(new ValidationFailure("limit", LimitMessage));
            return ReviewService.DefaultLimit;
        }

        return limit;
    }

    private static string RequireCustomerId(HttpContext context)
    {
        // `RequireAuth` đã chặn khách chưa đăng nhập. Nhân viên có phiên hợp lệ nhưng không mua hàng nên không đánh giá.
        var actor = context.GetActor();
        if (actor.Kind != ActorKind.Customer)
        {
            throw new AppException("FORBIDDEN", 403, CustomerOnlyMessage);
        }

        return actor.UserId!;
    }
}
