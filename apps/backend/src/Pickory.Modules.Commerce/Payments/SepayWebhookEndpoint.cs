using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Payments;

internal static class SepayWebhookEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Tiền tố webhook miễn `Origin`, nhưng khóa API vẫn bắt buộc trong handler.
        endpoints.MapPost($"{WebhookRoutes.PathPrefix}/sepay", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(HttpContext context, AppConfig config,
        SepayWebhookRateLimiter rateLimiter, SepayPaymentService payments, CancellationToken cancellationToken)
    {
        // Đếm IP trước xác thực để request dò khóa cũng chịu giới hạn tần suất.
        rateLimiter.Consume(context.GetClientIp());
        // Header sai phải dừng trước đọc body và trước mọi truy cập DB.
        if (!SepayWebhookAuthorization.Matches(config.SepayWebhookApiKey, context.Request.Headers.Authorization.ToString()))
        {
            throw new AppException("UNAUTHORIZED", 401, "Khóa webhook không hợp lệ.");
        }
        // Payload kiểm đủ giới hạn trước khi dịch vụ được phép lưu chứng từ.
        var payload = await SepayWebhookPayload.ReadAsync(context.Request, cancellationToken);
        using (payload.Raw)
        {
            // Dịch vụ lưu chứng từ rồi khớp đơn, chỉ trả thành công khi chứng từ đã nhận.
            await payments.ReceiveAsync(payload, cancellationToken);
        }
        // SePay yêu cầu body `success` trực tiếp, không bọc bằng `ApiResponse` của các route web.
        return Results.Text("{\"success\": true}", "application/json", statusCode: 200);
    }
}
