using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pickory.Modules.SupportDesk.Presence;

// Chạy nền trong API: mỗi 30 giây chuyển nhân viên mất nhịp tim sang `away`. MVP chạy một instance nên không cần khóa giữa nhiều worker.
internal sealed class StaffPresenceTimeoutWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<StaffPresenceTimeoutWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        do
        {
            try
            {
                // Mỗi vòng dùng scope mới để `DbContext` không giữ entity cũ giữa các lần chạy.
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<StaffPresenceTimeoutProcessor>();
                await processor.MarkStaleStaffAwayAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lỗi DB tạm thời không được làm dừng worker. Vòng sau sẽ thử lại.
                logger.LogError(exception, "Vòng chuyển nhân viên mất nhịp tim sang `away` bị lỗi.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
