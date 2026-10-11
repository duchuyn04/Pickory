using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Modules.Shipping.Shipments;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Shipping.Webhooks;

// Nhận cập nhật trạng thái từ GHN. Route không cần quyền vì GHN không đăng nhập, nên chỉ bí mật dùng chung chứng minh nguồn gọi.
internal static class GhnWebhookEndpoint
{
    public const string SecretHeaderName = "X-Pickory-Webhook-Secret";

    private const string OrderCodeField = "OrderCode";

    // Payload thật của GHN chỉ vài trăm byte. Giới hạn này chặn body lớn bất thường.
    private const int MaxBodyBytes = 64 * 1024;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost($"{WebhookRoutes.PathPrefix}/ghn", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        AppConfig config,
        GhnWebhookRateLimiter rateLimiter,
        ShippingDbContext database,
        IGhnClient ghnClient,
        ShipmentStatusUpdater statusUpdater,
        ILogger<GhnWebhookLog> logger,
        CancellationToken cancellationToken)
    {
        // Đếm mọi request, kể cả request sai secret. Kẻ dò secret bị chặn sau 300 lần thử mỗi phút.
        rateLimiter.Consume(context.GetClientIp());

        // Sai secret thì dừng trước khi đọc body hay chạm DB. Request không có quyền không tốn tài nguyên của DB.
        if (!SecretMatches(config.GhnWebhookSecret, context.Request.Headers[SecretHeaderName].ToString()))
        {
            throw new AppException("UNAUTHORIZED", 401, "Secret của webhook không hợp lệ.");
        }

        // Chỉ lấy mã vận đơn từ payload. Mọi trường khác, kể cả `Status`, do bên ngoài gửi nên không được tin.
        var orderCode = await ReadOrderCodeAsync(context.Request, cancellationToken);

        // Mã không có trong DB thì trả 200 để GHN không gửi lại. Webhook của đơn không thuộc module này không phải lỗi.
        var shipment = await database.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.TrackingCode == orderCode, cancellationToken);
        if (shipment is null)
        {
            return Ok();
        }

        GhnOrderDetail detail;
        try
        {
            // Lấy trạng thái thật từ API chi tiết đơn. `ShippingUnavailableException` đi thẳng lên và thành 503 để GHN gửi lại.
            detail = await ghnClient.GetOrderDetailAsync(orderCode, cancellationToken);
        }
        catch (ShippingRejectedException exception)
        {
            // GHN từ chối nghĩa là gửi lại không giúp được. Trả 200 và ghi cảnh báo để người vận hành đối chiếu.
            logger.LogWarning(
                exception,
                "GHN từ chối trả chi tiết đơn cho webhook. Mã vận đơn GHN {TrackingCode}.",
                orderCode);
            return Ok();
        }

        // Cùng một lớp áp trạng thái với job đồng bộ, nên webhook và job có chung luật chống trùng.
        await statusUpdater.ApplyAsync(shipment, detail.Status, ShipmentEventSource.Webhook);
        return Ok();
    }

    private static IResult Ok() => Results.Ok(new ApiResponse<object>(new { received = true }));

    // Băm cả hai giá trị trước khi so, để thời gian so và độ dài không lộ độ dài secret. Secret rỗng nghĩa là server chưa cấu hình.
    private static bool SecretMatches(string expectedSecret, string providedSecret)
    {
        if (string.IsNullOrEmpty(expectedSecret))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedSecret));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedSecret));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }

    private static async Task<string> ReadOrderCodeAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var body = await ReadBoundedBodyAsync(request, cancellationToken);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw Invalid("Body không phải JSON hợp lệ.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("Body phải là một đối tượng JSON.");
            }

            // Tên trường của GHN là PascalCase. So không phân biệt hoa thường để không lệch khi GHN đổi kiểu viết.
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, OrderCodeField, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        throw Invalid("Thiếu mã vận đơn.");
    }

    // Đọc tối đa `MaxBodyBytes` byte. Body dài hơn bị từ chối thay vì đọc hết vào bộ nhớ.
    private static async Task<byte[]> ReadBoundedBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBodyBytes)
            {
                throw Invalid("Body quá lớn.");
            }
        }

        return buffer.ToArray();
    }

    private static ValidationException Invalid(string message) =>
        new([new ValidationFailure(OrderCodeField, message)]);
}

// Kiểu đánh dấu nhóm log của webhook, vì lớp endpoint là `static` nên không dùng được làm tham số của `ILogger<T>`.
internal sealed class GhnWebhookLog;
