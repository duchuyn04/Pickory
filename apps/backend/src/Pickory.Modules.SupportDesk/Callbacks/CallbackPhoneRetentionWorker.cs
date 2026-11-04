using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Chạy nền trong API: mỗi ngày xóa số điện thoại của các yêu cầu gọi lại quá hạn 30 ngày.
internal sealed class CallbackPhoneRetentionWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<CallbackPhoneRetentionWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        do
        {
            try
            {
                // Mỗi vòng dùng scope mới để `DbContext` không giữ dữ liệu của ngày trước.
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<ICallbackPhoneRetentionProcessor>();
                await processor.RedactExpiredPhonesAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lỗi DB tạm thời không được làm dừng worker. Vòng kế tiếp sẽ thử lại.
                logger.LogError(exception, "Vòng xóa số điện thoại quá hạn bị lỗi.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
