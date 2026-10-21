using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.ActivityTracking.Counters;
using Pickory.Modules.Identity.Contracts.Actors;

namespace Pickory.Modules.ActivityTracking.Features.CollectEvents;

// Nhận lượt xem sản phẩm. Route công khai vì khách chưa đăng nhập cũng xem sản phẩm.
internal static class CollectEventsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/events", HandleAsync);
    }

    internal static async Task<IResult> HandleAsync(
        HttpContext context,
        ProductViewRateLimiter rateLimiter,
        DailyCounterWriter counterWriter,
        CancellationToken cancellationToken)
    {
        // Kiểm body trước mọi bước khác. Body sai trả 400, body quá lớn trả 413, và cả hai không tốn lượt giới hạn.
        var request = await CollectEventRequest.ReadAsync(context.Request.Body, context.Request.ContentLength, cancellationToken);

        // Lấy loại người gửi từ actor middleware. Giá trị client gửi trong body không được tin.
        var actor = context.GetActor();

        // Nhân viên xem sản phẩm khi quản trị. Trả 204 để client không phân biệt được, nhưng bộ đếm không đổi.
        if (actor.Kind == ActorKind.Staff)
        {
            return Results.NoContent();
        }

        // Crawler không phải khách thật. Trả 204 và bỏ qua trước giới hạn tần suất, để crawler không chiếm hạn mức.
        if (CrawlerUserAgents.IsCrawler(context.Request.Headers.UserAgent.ToString()))
        {
            return Results.NoContent();
        }

        // Giới hạn theo `AnonymousId` của actor. Hết lượt thì limiter ném 429 trước khi bộ đếm đổi.
        rateLimiter.Consume(actor.AnonymousId);

        // Ghi bộ đếm không theo token của request. Client ngắt kết nối sau khi gửi xong body thì lượt xem vẫn hợp lệ.
        await counterWriter.IncrementAsync(request.Type, CancellationToken.None);
        return Results.NoContent();
    }
}
