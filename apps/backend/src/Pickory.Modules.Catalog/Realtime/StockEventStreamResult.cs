using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pickory.Modules.Catalog.Realtime;

// Ghi luồng SSE ra response. Kết quả có sẵn của ASP.NET Core không gửi header cho tới khi có sự kiện đầu, nên trình duyệt không biết đã kết nối.
internal sealed class StockEventStreamResult(IAsyncEnumerable<SseItem<object>> items) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        // Không cho trình duyệt hay proxy lưu luồng. Sự kiện cũ được phát lại sẽ làm sai số tồn trên màn hình.
        response.Headers.CacheControl = "no-cache";
        // Nginx giữ đệm response theo mặc định. Header này bắt nó chuyển sự kiện đi ngay.
        response.Headers["X-Accel-Buffering"] = "no";

        // Báo cho máy chủ hoặc middleware có bộ đệm riêng gửi dữ liệu ngay. Kết quả SSE có sẵn của framework cũng gọi bước này.
        httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();

        // Gửi header ngay. Client biết kết nối đã mở mà không phải chờ nhịp tim đầu tiên.
        await response.StartAsync(httpContext.RequestAborted);
        // Đẩy luôn bộ đệm. Một số host chỉ gửi header cho client sau lần flush đầu tiên.
        await response.Body.FlushAsync(httpContext.RequestAborted);

        // Dùng cấu hình JSON chung của API. Tên trường trong dữ liệu sự kiện là camelCase như response thường.
        var jsonOptions = httpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        await SseFormatter.WriteAsync(
            items,
            response.Body,
            (item, writer) =>
            {
                using var jsonWriter = new Utf8JsonWriter(writer);
                JsonSerializer.Serialize(jsonWriter, item.Data, jsonOptions);
            },
            httpContext.RequestAborted);
    }
}
