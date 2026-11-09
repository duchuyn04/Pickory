using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pickory.Modules.SupportDesk.Sessions;

// Chạy nền trong API: mỗi phút nhắc lại các phiên chờ quá lâu. MVP chạy một instance nên không cần khóa giữa nhiều worker.
internal sealed class WaitNudgeWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<WaitNudgeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        do
        {
            try
            {
                // Mỗi vòng dùng scope mới để `DbContext` không giữ dữ liệu phiên của vòng trước.
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<WaitNudgeProcessor>();
                await processor.RemindWaitingSessionsAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lỗi DB tạm thời không được làm dừng worker. Vòng sau sẽ thử lại.
                logger.LogError(exception, "Vòng nhắc lại phiên chờ bị lỗi.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
