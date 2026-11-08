using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Conversations;

// SD-17: kiểm mốc 24 giờ, 90 ngày và vòng lặp mỗi ngày của `SupportConversationMaintenanceWorker` bằng đồng hồ giả.
public sealed class SupportConversationMaintenanceWorkerTests
{
    private static readonly TimeSpan JustBeforeOneDay = TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59);
    private static readonly TimeSpan NegativeCheckWait = TimeSpan.FromMilliseconds(100);

    private static (SupportConversationMaintenanceWorker Worker, StubProcessor Processor, FakeTimeProvider Clock, RecordingLogger<SupportConversationMaintenanceWorker> Logger) CreateWorker()
    {
        var processor = new StubProcessor();
        var services = new ServiceCollection();
        // Đăng ký processor giả vào container để worker lấy qua scope của mỗi vòng.
        services.AddScoped<ISupportConversationMaintenanceProcessor>(_ => processor);
        var provider = services.BuildServiceProvider();

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger<SupportConversationMaintenanceWorker>();
        var worker = new SupportConversationMaintenanceWorker(provider.GetRequiredService<IServiceScopeFactory>(), clock, logger);
        return (worker, processor, clock, logger);
    }

    [Fact]
    // Mốc 24 giờ phải trừ chính xác 24 giờ từ thời điểm hiện tại.
    public void GetInactiveCutoffUtc_SubtractsExactly24Hours()
    {
        var now = new DateTime(2026, 11, 8, 10, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 11, 7, 10, 30, 0, DateTimeKind.Utc), SupportConversationMaintenanceProcessor.GetInactiveCutoffUtc(now));
    }

    [Fact]
    // Mốc 90 ngày phải trừ chính xác 90 ngày từ thời điểm hiện tại.
    public void GetRetentionCutoffUtc_SubtractsExactly90Days()
    {
        var now = new DateTime(2026, 11, 8, 10, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 8, 10, 10, 30, 0, DateTimeKind.Utc), SupportConversationMaintenanceProcessor.GetRetentionCutoffUtc(now));
    }

    [Fact]
    // Worker chạy ngay khi khởi động, rồi lặp lại mỗi 24 giờ theo đồng hồ giả mà không chờ giờ thật.
    public async Task Worker_RunsOnStartup_ThenEvery24Hours()
    {
        var (worker, processor, clock, _) = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Vòng đầu chạy ngay khi worker vừa khởi động.
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 1);

            // Chưa đủ 24 giờ thì worker không chạy vòng thứ hai.
            clock.Advance(JustBeforeOneDay);
            await Task.Delay(NegativeCheckWait);
            Assert.Equal(1, processor.CallCount);

            // Nhích thêm 1 phút cho đủ tròn 24 giờ thì vòng thứ hai chạy.
            clock.Advance(TimeSpan.FromMinutes(1));
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 2);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    // Worker ghi log `Error` khi gặp lỗi tạm thời ở một vòng và tiếp tục chạy ngày hôm sau.
    public async Task Worker_KeepsRunningAfterFailure_AndLogsError()
    {
        var (worker, processor, clock, logger) = CreateWorker();
        processor.ShouldThrowOnFirstCall = true;

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Vòng đầu gặp lỗi nhưng không làm dừng tiến trình.
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 1);
            await FakeTimeDriver.WaitUntilAsync(() => logger.RecordedLevels.Contains(LogLevel.Error));

            // Ngày hôm sau worker vẫn tiếp tục chạy vòng mới bình thường.
            clock.Advance(SupportConversationMaintenanceWorker.PollInterval);
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 2);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class StubProcessor : ISupportConversationMaintenanceProcessor
    {
        public int CallCount { get; private set; }
        public bool ShouldThrowOnFirstCall { get; set; }

        public Task<SupportConversationMaintenanceResult> MaintainConversationsAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            if (ShouldThrowOnFirstCall && CallCount == 1)
            {
                // Giả lập lỗi DB tạm thời ở vòng đầu để kiểm tra tính bền bỉ của worker.
                throw new InvalidOperationException("DB tạm thời không kết nối được.");
            }

            return Task.FromResult(new SupportConversationMaintenanceResult(0, 0));
        }
    }
}
