using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pickory.Modules.Commerce.Payments;

// Scope riêng mỗi phút tránh giữ entity cũ khi webhook đã đổi trạng thái đơn.
public sealed class ExpiredPaymentWorker(
    IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<ExpiredPaymentWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Timer dùng đồng hồ chung để chu kỳ không phụ thuộc đồng hồ tĩnh của hệ điều hành.
        using var timer = new PeriodicTimer(PollInterval, clock);
        do
        {
            try
            {
                // Context mới mỗi vòng buộc truy vấn hết hạn đọc dữ liệu đã commit gần nhất.
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ExpiredPaymentProcessor>().ProcessAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lỗi chọn đơn không được dừng worker; vòng phút tiếp theo đọc lại DB.
                logger.LogError(exception, "Vòng hủy đơn hết hạn thanh toán thất bại.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
