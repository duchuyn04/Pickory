using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pickory.Modules.Catalog.Realtime;

// Luồng sự kiện tồn kho công khai. Trang khách và trang quản lý sản phẩm cùng nghe luồng này.
internal static class StockEventsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Luồng không kết thúc, client đóng kết nối khi rời màn hình hoặc khi trình duyệt đóng tab.
        endpoints.MapGet("/api/stock-events", OpenEventStream)
            // Khai báo kiểu nội dung để tài liệu OpenAPI mô tả đúng luồng SSE. Hàm trả `IResult` nên OpenAPI không tự suy ra kiểu nội dung.
            .Produces<string>(StatusCodes.Status200OK, "text/event-stream");
    }

    internal static IResult OpenEventStream(HttpContext context, StockEventStreamer streamer)
    {
        return new StockEventStreamResult(streamer.StreamAsync(context.RequestAborted));
    }
}
