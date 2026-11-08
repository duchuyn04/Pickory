using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pickory.Modules.SupportDesk.Conversations;

// Chạy nền trong API: mỗi ngày đóng hội thoại im lặng 24 giờ và xóa hội thoại cũ hơn 90 ngày.
internal sealed class SupportConversationMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<SupportConversationMaintenanceWorker> logger) : BackgroundService
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
                var processor = scope.ServiceProvider.GetRequiredService<ISupportConversationMaintenanceProcessor>();
                await processor.MaintainConversationsAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lỗi DB tạm thời không được làm dừng worker. Vòng kế tiếp sẽ thử lại.
                logger.LogError(exception, "Vòng bảo trì hội thoại CSKH bị lỗi.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
