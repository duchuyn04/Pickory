using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pickory.Modules.SupportDesk.Realtime;

// Ghi luồng SSE phía khách ra response, gồm sự kiện và dòng chú thích giữ kết nối.
// Kết quả có sẵn của ASP.NET Core không gửi header cho tới khi có sự kiện đầu, nên trình duyệt không biết đã kết nối.
internal sealed class CustomerEventStreamResult(IAsyncEnumerable<CustomerStreamItem> items) : IResult
{
    // Dòng chú thích giữ kết nối. Client bỏ qua dòng bắt đầu bằng `:`, còn proxy coi kết nối còn sống.
    private static readonly byte[] KeepAliveLine = ": keep-alive\n\n"u8.ToArray();
    private static readonly byte[] DataPrefix = "data: "u8.ToArray();
    private static readonly byte[] EventEnd = "\n\n"u8.ToArray();

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        // Không cho trình duyệt hay proxy lưu luồng. Sự kiện cũ được phát lại sẽ làm sai trạng thái khung chat.
        response.Headers.CacheControl = "no-cache";
        // Nginx giữ đệm response theo mặc định. Header này bắt nó chuyển sự kiện đi ngay.
        response.Headers["X-Accel-Buffering"] = "no";

        // Báo cho máy chủ hoặc middleware có bộ đệm riêng gửi dữ liệu ngay.
        httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();

        // Gửi header ngay. Client biết kết nối đã mở mà không phải chờ chú thích đầu tiên sau 15 giây.
        await response.StartAsync(httpContext.RequestAborted);
        await response.Body.FlushAsync(httpContext.RequestAborted);

        // Dùng cấu hình JSON chung của API. Tên trường trong dữ liệu sự kiện là camelCase như response thường.
        // Sao chép cấu hình và tắt thụt lề, vì dòng `data:` chỉ nhận một dòng JSON.
        var jsonOptions = new JsonSerializerOptions(
            httpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions)
        {
            WriteIndented = false,
        };
        await WriteItemsAsync(response.Body, jsonOptions, httpContext.RequestAborted);
    }

    private async Task WriteItemsAsync(
        Stream body, JsonSerializerOptions jsonOptions, CancellationToken cancellationToken)
    {
        await foreach (var item in items.WithCancellation(cancellationToken))
        {
            switch (item)
            {
                case CustomerStreamItem.KeepAliveComment:
                    await body.WriteAsync(KeepAliveLine, cancellationToken);
                    break;
                case CustomerStreamItem.Event supportEvent:
                    await WriteEventAsync(body, supportEvent, jsonOptions, cancellationToken);
                    break;
                default:
                    // Kiểu dòng đóng nên nhánh này là lỗi lập trình. Bỏ qua sẽ làm client thiếu dữ liệu mà không báo.
                    throw new InvalidOperationException($"Loại dòng SSE phía khách không hỗ trợ: {item.GetType().Name}.");
            }

            // Đẩy từng dòng ra client. Bộ đệm giữ sự kiện lại làm chậm tin của nhân viên.
            await body.FlushAsync(cancellationToken);
        }
    }

    private static async Task WriteEventAsync(
        Stream body,
        CustomerStreamItem.Event supportEvent,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        // Tên sự kiện vào trường `event` của SSE. Client dùng tên này để chọn bộ nhận sự kiện.
        await body.WriteAsync(Encoding.UTF8.GetBytes($"event: {supportEvent.Name}\n"), cancellationToken);
        // Dữ liệu nằm trên một dòng `data:` riêng. Cấu hình đã tắt thụt lề nên JSON không xuống dòng.
        await body.WriteAsync(DataPrefix, cancellationToken);
        await JsonSerializer.SerializeAsync(
            body, supportEvent.Data, supportEvent.Data.GetType(), jsonOptions, cancellationToken);
        await body.WriteAsync(EventEnd, cancellationToken);
    }
}
