using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.Shipping.Addresses;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Addresses;

// SHIP-04 lát 6: worker đồng bộ một lần khi khởi động, rồi mỗi 24 giờ, và lỗi không làm dừng worker.
public sealed class AddressSyncWorkerTests
{
    private static readonly TimeSpan JustBeforeInterval = TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59);

    // Thời gian chờ thật để chứng minh "chưa chạy". Chỉ làm test dễ pass sai, không làm test fail sai.
    private static readonly TimeSpan NegativeCheckWait = TimeSpan.FromMilliseconds(100);

    private static (AddressSyncWorker Worker, StubAddressSyncProcessor Processor, FakeTimeProvider Time, RecordingLogger<AddressSyncWorker> Logger) CreateWorker()
    {
        var processor = new StubAddressSyncProcessor();
        var services = new ServiceCollection();
        services.AddScoped<IAddressSyncProcessor>(_ => processor);
        var provider = services.BuildServiceProvider();

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger<AddressSyncWorker>();
        var worker = new AddressSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), time, logger);
        return (worker, processor, time, logger);
    }

    [Fact]
    public void SyncInterval_Is24Hours()
    {
        Assert.Equal(TimeSpan.FromHours(24), AddressSyncWorker.SyncInterval);
    }

    [Fact]
    // Dùng đồng hồ giả để vòng thứ hai chạy ngay khi đẩy giờ. Test không chờ 24 giờ thật.
    public async Task Worker_SyncsOnStartup_ThenEvery24Hours()
    {
        var (worker, processor, time, _) = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 1);

            // Chưa đủ 24 giờ thì chưa có vòng thứ hai.
            time.Advance(JustBeforeInterval);
            await Task.Delay(NegativeCheckWait);
            Assert.Equal(1, processor.CallCount);

            // Đủ 24 giờ thì vòng thứ hai chạy.
            time.Advance(TimeSpan.FromMinutes(1));
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 2);

            // Thêm 24 giờ nữa thì có vòng thứ ba, nên chu kỳ lặp lại chứ không chạy đúng hai lần.
            time.Advance(AddressSyncWorker.SyncInterval);
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
            : AddressSyncResult.Success(1, 1);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 1);
            await FakeTimeDriver.WaitUntilAsync(() => logger.RecordedLevels.Contains(LogLevel.Error));

            time.Advance(AddressSyncWorker.SyncInterval);
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 2);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }
}
