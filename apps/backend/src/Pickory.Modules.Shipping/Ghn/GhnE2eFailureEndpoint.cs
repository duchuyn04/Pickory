using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pickory.Modules.Shipping.Ghn;

// Route chỉ dành cho Playwright: bật hoặc tắt lỗi "GHN không phản hồi" ở hàm báo phí của `FakeGhnClient`.
// Route làm hỏng báo phí mà không cần đăng nhập, nên `Program.cs` chỉ gọi hàm map khi API chạy bằng lệnh `e2e:start`.
internal static class GhnE2eFailureEndpoint
{
    public const string CalculateFeeRoute = "/api/e2e/ghn/failures/calculate-fee";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Cùng điều kiện với route đổi trạng thái đơn: không có `GHN_TOKEN` và đang dùng client giả.
        if (!GhnE2eStatusEndpoint.CanMapFakeGhnRoutes(endpoints.ServiceProvider))
        {
            return;
        }

        endpoints.MapPut(CalculateFeeRoute, EnableCalculateFeeFailure);
        endpoints.MapDelete(CalculateFeeRoute, DisableCalculateFeeFailure);
    }

    private static IResult EnableCalculateFeeFailure(FakeGhnClient fakeGhnClient)
    {
        // `Unavailable` giả lập GHN hết thời gian chờ hoặc lỗi máy chủ. Đây là lỗi duy nhất kích hoạt phí đã lưu và phí dự phòng.
        fakeGhnClient.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Unavailable);
        return Results.NoContent();
    }

    private static IResult DisableCalculateFeeFailure(FakeGhnClient fakeGhnClient)
    {
        // Chỉ tắt lỗi báo phí. Lỗi của các hàm khác mà test khác đã bật vẫn giữ nguyên.
        fakeGhnClient.ClearFailure(FakeGhnOperation.CalculateFee);
        return Results.NoContent();
    }
}
