using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pickory.Modules.SupportDesk.Sessions;

// Worker chạy nền định kỳ mỗi phút để tự đóng các phiên im lặng và phiên chờ ngoài giờ quá hạn.
internal sealed class AutoCloseSessionWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AutoCloseSessionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Mỗi vòng dùng scope mới để giải phóng `DbContext` cũ.
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<AutoCloseSessionProcessor>();
                await processor.CloseInactiveSessionsAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Ghi nhận lỗi vòng chạy để không làm dừng worker nền.
                logger.LogError(exception, "Vòng tự đóng phiên im lặng bị lỗi.");
            }
        }
    }
}
