using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Shipping.Http;
using Pickory.Modules.Shipping.Shipments;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Shipping.Ghn;

// Route chỉ dành cho Playwright: đổi trạng thái gốc của một đơn trong `FakeGhnClient`, như GHN đổi trạng thái sau khi shipper giao hàng.
// Route đổi trạng thái đơn mà không cần đăng nhập, nên `Program.cs` chỉ gọi hàm map khi API chạy bằng lệnh `e2e:start`.
internal static class GhnE2eStatusEndpoint
{
    public const string Route = "/api/e2e/ghn/orders/{trackingCode}/status";

    private const string StatusField = "status";

    // Body chỉ có một chuỗi nhỏ. Giới hạn này chặn body lớn bất thường.
    private const int MaxBodyBytes = 4 * 1024;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var services = endpoints.ServiceProvider;

        // Có `GHN_TOKEN` thì API đang gọi GHN thật. Đổi trạng thái giả khi đó không có nghĩa, nên không map route.
        if (!string.IsNullOrWhiteSpace(services.GetRequiredService<AppConfig>().GhnToken))
        {
            return;
        }

        // Thiếu `FakeGhnClient` trong DI nghĩa là client thật đang được dùng. Không có đối tượng để đổi trạng thái thì không map route.
        if (services.GetService<FakeGhnClient>() is null)
        {
            return;
        }

        endpoints.MapPost(Route, HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string trackingCode,
        HttpContext context,
        FakeGhnClient fakeGhnClient,
        CancellationToken cancellationToken)
    {
        // Đọc và kiểm trạng thái trước khi chạm client giả, để body sai không đổi gì.
        var rawStatus = await ReadStatusAsync(context.Request, cancellationToken);

        // Trạng thái không có trong bảng ánh xạ thì webhook sau đó cũng không áp được. Báo lỗi sớm cho người viết test.
        if (ShipmentStatusMapper.Map(rawStatus) is null)
        {
            throw Invalid("Trạng thái GHN không có trong bảng ánh xạ.");
        }

        // Mã vận đơn lạ trả 404 rõ ràng. Client giả ném lỗi chung với mã lạ, nên dùng hàm trả `bool`.
        if (!fakeGhnClient.TrySetOrderStatus(trackingCode, rawStatus))
        {
            throw new AppException("SHIPMENT_NOT_FOUND", 404, "Không có vận đơn giả với mã này.");
        }

        return Results.Ok(new ApiResponse<object>(new { trackingCode, status = rawStatus }));
    }

    private static async Task<string> ReadStatusAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        // Body vượt giới hạn bị từ chối bằng lỗi validation của trường `status`.
        var body = await BoundedRequestBody.ReadAsync(request, MaxBodyBytes, cancellationToken)
            ?? throw Invalid("Body quá lớn.");

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
            // Trạng thái phải là chuỗi. Số, mảng hay đối tượng không phải trạng thái gốc của GHN.
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(StatusField, out var status)
                && status.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(status.GetString()))
            {
                return status.GetString()!;
            }
        }

        throw Invalid("Cần trường status là chuỗi không rỗng.");
    }

    private static ValidationException Invalid(string message) =>
        new([new ValidationFailure(StatusField, message)]);
}
