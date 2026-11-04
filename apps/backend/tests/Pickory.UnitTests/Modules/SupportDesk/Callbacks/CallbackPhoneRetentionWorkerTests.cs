using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.SupportDesk.Callbacks;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Callbacks;

// SD-05: kiểm mốc 30 ngày và vòng lặp mỗi ngày của `CallbackPhoneRetentionWorker` bằng đồng hồ giả.
public sealed class CallbackPhoneRetentionWorkerTests
{
    private static readonly TimeSpan JustBeforeOneDay = TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59);
    private static readonly TimeSpan NegativeCheckWait = TimeSpan.FromMilliseconds(100);

    private static (CallbackPhoneRetentionWorker Worker, StubProcessor Processor, FakeTimeProvider Clock, RecordingLogger<CallbackPhoneRetentionWorker> Logger) CreateWorker()
    {
        var processor = new StubProcessor();
        var services = new ServiceCollection();
        services.AddScoped<ICallbackPhoneRetentionProcessor>(_ => processor);
        var provider = services.BuildServiceProvider();

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger<CallbackPhoneRetentionWorker>();
        var worker = new CallbackPhoneRetentionWorker(provider.GetRequiredService<IServiceScopeFactory>(), clock, logger);
        return (worker, processor, clock, logger);
    }

    [Fact]
    public void GetCutoffUtc_SubtractsExactly30Days()
    {
        var now = new DateTime(2026, 11, 3, 10, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Utc), CallbackPhoneRetentionProcessor.GetCutoffUtc(now));
    }

    [Fact]
    // Worker chạy ngay khi khởi động, rồi lặp lại mỗi 24 giờ theo đồng hồ giả mà không chờ giờ thật.
    public async Task Worker_RunsOnStartup_ThenEvery24Hours()
    {
        var (worker, processor, clock, _) = CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 1);

            clock.Advance(JustBeforeOneDay);
            await Task.Delay(NegativeCheckWait);
            Assert.Equal(1, processor.CallCount);

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
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 1);
            await FakeTimeDriver.WaitUntilAsync(() => logger.RecordedLevels.Contains(LogLevel.Error));

            clock.Advance(CallbackPhoneRetentionWorker.PollInterval);
            await FakeTimeDriver.WaitUntilAsync(() => processor.CallCount == 2);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class StubProcessor : ICallbackPhoneRetentionProcessor
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public bool ShouldThrowOnFirstCall { get; set; }

        public Task<int> RedactExpiredPhonesAsync(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _callCount);
            if (ShouldThrowOnFirstCall && current == 1)
            {
                throw new InvalidOperationException("DB tạm thời không kết nối được.");
            }

            return Task.FromResult(0);
        }
    }
}
