using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pickory.Modules.Shipping.Sync;

// Tra lại trạng thái vận đơn khi API khởi động, rồi lặp lại mỗi 30 phút. MVP chạy một instance nên không cần khóa giữa các worker.
internal sealed class ShipmentStatusSyncWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ShipmentStatusSyncWorker> logger) : BackgroundService
{
    // Chu kỳ 30 phút theo spec. Webhook là đường chính, job chỉ bù khi webhook bị mất.
    internal static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SyncInterval, timeProvider);
        do
        {
            try
            {
                // Mỗi vòng dùng scope mới để `DbContext` không giữ entity cũ giữa các lần chạy.
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IShipmentStatusSyncProcessor>();
                var result = await processor.SyncAsync(stoppingToken);
                logger.LogInformation(
                    "Vòng đồng bộ trạng thái vận đơn xong. Đã tra {Checked}, đã đổi {Changed}, lỗi {Failed}.",
                    result.Checked,
                    result.Changed,
                    result.Failed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lỗi DB tạm thời không được làm dừng worker. Vòng sau sẽ thử lại.
                logger.LogError(exception, "Vòng đồng bộ trạng thái vận đơn bị lỗi.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
