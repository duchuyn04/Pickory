using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Pickory.Modules.Shipping;

namespace Pickory.IntegrationTests.Infrastructure;

// Giả lập điều kiện `e2e:start` của `Program.cs`: filter gắn route E2E của Shipping vào host test.
// Host test không chạy bằng lệnh `e2e:start` nên nếu thiếu filter này route không bao giờ được map.
public sealed class E2eShippingRoutesStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Gắn sau pipeline của app để request vẫn đi qua actor middleware (kiểm `Origin`) như khi chạy thật.
            next(app);

            // Pipeline của app đã khớp route trước đó, nên cần một lượt định tuyến mới để khớp route E2E gắn thêm.
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapShippingE2eEndpoints());
        };
    }
}
