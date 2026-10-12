using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Payments;
using Pickory.Modules.Commerce.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ExpiredPaymentWorkerTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private readonly PaymentExpiryTestKit _kit = new(factory, database);
    public Task InitializeAsync() => _kit.InitializeAsync();
    public async Task DisposeAsync() => await _kit.DisposeAsync();

    [Fact]
    public async Task Worker_ExpiresAtOneMinuteButNotAtFiftyNineSeconds()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), ExpiredPaymentWorker.PollInterval);
        var order = await _kit.CreateOrderAsync(deadline: PaymentExpiryTestKit.Now.UtcDateTime.AddSeconds(30));
        var probe = new FirstOrderRead();
        var host = _kit.CreateHost(services => services.AddDbContext<CommerceDbContext>(options => options.AddInterceptors(probe)));
        var timerClock = new RecordingTimerClock(_kit.Clock);
        using var worker = new ExpiredPaymentWorker(host.Services.GetRequiredService<IServiceScopeFactory>(), timerClock,
            NullLogger<ExpiredPaymentWorker>.Instance);
        try
        {
            await worker.StartAsync(CancellationToken.None);
            // Đợi truy vấn vòng đầu hoàn tất để đẩy giờ giả không thay đổi mốc chọn đơn của vòng đó.
            await probe.Read.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Kiểm lịch timer trực tiếp để assertion âm không phụ thuộc tốc độ tiếp tục của task DB.
            Assert.Equal(ExpiredPaymentWorker.PollInterval, timerClock.DueTime);
            Assert.Equal(ExpiredPaymentWorker.PollInterval, timerClock.Period);
            _kit.Clock.Advance(TimeSpan.FromSeconds(59));
            Assert.Equal(OrderStatus.PendingPayment, (await _kit.ReadOrderAsync(order.Id)).Status);
            Assert.Empty(_kit.Events.Observations);
            // Timer thật dùng đồng hồ giả; tick thứ nhất phải chạy tại giây 60, không cần ngủ.
            _kit.Clock.Advance(TimeSpan.FromSeconds(1));
            await _kit.Events.Changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(OrderStatus.Cancelled, (await _kit.ReadOrderAsync(order.Id)).Status);
            Assert.Equal(10, await _kit.StockAsync(order.SkuId));
            Assert.Single(_kit.Events.Observations);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FirstOrderRead : DbCommandInterceptor
    {
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM orders", StringComparison.Ordinal)) Read.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingTimerClock(TimeProvider inner) : TimeProvider
    {
        public TimeSpan DueTime { get; private set; }
        public TimeSpan Period { get; private set; }
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            Period = period;
            // Giữ timer của đồng hồ giả để kiểm lịch mà vẫn chạy callback thật.
            return inner.CreateTimer(callback, state, dueTime, period);
        }
    }
}
