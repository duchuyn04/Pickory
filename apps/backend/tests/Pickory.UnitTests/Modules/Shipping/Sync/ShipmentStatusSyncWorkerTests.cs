using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.Shipping.Sync;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Sync;

// SHIP-08 lát 4: worker tra lại trạng thái một lần khi khởi động, rồi mỗi 30 phút, và lỗi không làm dừng worker.
public sealed class ShipmentStatusSyncWorkerTests
{
    private static readonly TimeSpan JustBeforeInterval = TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59);

    // Thời gian chờ thật để chứng minh "chưa chạy". Chỉ làm test dễ pass sai, không làm test fail sai.
    private static readonly TimeSpan NegativeCheckWait = TimeSpan.FromMilliseconds(100);

    private static (ShipmentStatusSyncWorker Worker, StubShipmentStatusSyncProcessor Processor, FakeTimeProvider Time, RecordingLogger<ShipmentStatusSyncWorker> Logger) CreateWorker()
    {
        var processor = new StubShipmentStatusSyncProcessor();
        var services = new ServiceCollection();
        services.AddScoped<IShipmentStatusSyncProcessor>(_ => processor);
        var provider = services.BuildServiceProvider();

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger<ShipmentStatusSyncWorker>();
        var worker = new ShipmentStatusSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), time, logger);
        return (worker, processor, time, logger);
    }

    [Fact]
    public void SyncInterval_Is30Minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), ShipmentStatusSyncWorker.SyncInterval);
    }

    [Fact]
    public async Task Worker_SyncsOnStartup_ThenEvery30Minutes()
    {
        var (worker, processor, time, _) = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 1);

            // Chưa đủ 30 phút thì chưa có vòng thứ hai.
            time.Advance(JustBeforeInterval);
            await Task.Delay(NegativeCheckWait);
            Assert.Equal(1, processor.CallCount);

            // Đủ 30 phút thì vòng thứ hai chạy.
            time.Advance(TimeSpan.FromSeconds(1));
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 2);

            // Thêm 30 phút nữa thì có vòng thứ ba, nên chu kỳ lặp lại chứ không chạy đúng hai lần.
            time.Advance(ShipmentStatusSyncWorker.SyncInterval);
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 3);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_KeepsRunning_AfterProcessorThrows_AndLogsError()
    {
        var (worker, processor, time, logger) = CreateWorker();
        processor.OnSync = callNumber => callNumber == 1
            ? throw new InvalidOperationException("DB tạm thời không kết nối được")
            : new ShipmentStatusSyncResult(1, 1, 0);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 1);
            await FakeTimeDriver.WaitUntilAsync(() => logger.RecordedLevels.Contains(LogLevel.Error));

            time.Advance(ShipmentStatusSyncWorker.SyncInterval);
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 2);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }
}
