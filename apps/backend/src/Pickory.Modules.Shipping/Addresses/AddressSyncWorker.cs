using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pickory.Modules.Shipping.Addresses;

// Đồng bộ danh mục địa chỉ khi API khởi động, rồi lặp lại mỗi 24 giờ. MVP chạy một instance nên không cần khóa giữa các worker.
internal sealed class AddressSyncWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AddressSyncWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan SyncInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SyncInterval, timeProvider);
        do
        {
            try
            {
                // Mỗi vòng dùng scope mới để `DbContext` không giữ entity cũ giữa các lần chạy.
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IAddressSyncProcessor>();
                await processor.SyncAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lỗi DB tạm thời không được làm dừng worker. Vòng sau sẽ thử lại.
                logger.LogError(exception, "Vòng đồng bộ danh mục địa chỉ bị lỗi.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
