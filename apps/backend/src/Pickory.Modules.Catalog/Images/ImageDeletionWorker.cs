using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pickory.Modules.Catalog.Images;

// Chạy nền trong API: định kỳ xóa ảnh của các job đến hạn. MVP chạy một instance nên không cần khóa giữa nhiều worker.
public sealed class ImageDeletionWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ImageDeletionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        do
        {
            try
            {
                // Mỗi vòng dùng scope mới để `DbContext` không giữ entity cũ giữa các lần chạy.
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<ImageDeletionProcessor>();
                await processor.ProcessDueJobsAsync(BatchSize, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lỗi DB tạm thời không được làm dừng worker. Vòng sau sẽ thử lại.
                logger.LogError(exception, "Vòng chạy job xóa ảnh bị lỗi.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
