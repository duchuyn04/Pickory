using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Payments;
using Pickory.Modules.Commerce.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ExpiredPaymentTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private readonly PaymentExpiryTestKit _kit = new(factory, database);
    public Task InitializeAsync() => _kit.InitializeAsync();
    public async Task DisposeAsync() => await _kit.DisposeAsync();

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task DeadlineBoundary_ExpiresOnlyDueOrdersAndEmitsAfterCommit(int seconds, bool expires)
    {
        var order = await _kit.CreateOrderAsync(deadline: PaymentExpiryTestKit.Now.UtcDateTime.AddSeconds(seconds));
        var host = _kit.CreateHost();
        using var scope = host.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ExpiredPaymentProcessor>();
        // Chạy hai vòng chứng minh cờ, lịch sử và hàng trả không bị ghi lặp.
        await processor.ProcessAsync(CancellationToken.None);
        await processor.ProcessAsync(CancellationToken.None);
        var saved = await _kit.ReadOrderAsync(order.Id);
        Assert.Equal(expires ? OrderStatus.Cancelled : OrderStatus.PendingPayment, saved.Status);
        Assert.Equal(PaymentStatus.Unpaid, saved.PaymentStatus);
        Assert.Equal(expires ? 10 : 8, await _kit.StockAsync(order.SkuId));
        if (!expires)
        {
            Assert.DoesNotContain("payment_expired", saved.Flags);
            Assert.Empty(_kit.Events.Observations);
            Assert.Single(saved.StatusHistory);
            return;
        }
        Assert.Contains("payment_expired", saved.Flags);
        var history = Assert.Single(saved.StatusHistory, row => row.ToStatus == OrderStatus.Cancelled);
        Assert.Equal("Quá hạn thanh toán.", history.Reason);
        Assert.Equal(PaymentExpiryTestKit.Now.UtcDateTime, history.CreatedAt);
        var observed = Assert.Single(_kit.Events.Observations);
        Assert.False(observed.PaidEvent);
        Assert.Equal(OrderStatus.Cancelled, observed.Status);
        Assert.Equal(10, observed.Stock);
        Assert.Equal(2, observed.HistoryCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CodOrPaidOrder_IsNotExpired(bool cod)
    {
        var order = await _kit.CreateOrderAsync(cod ? "cod" : "sepay");
        await using (var commerce = database.CreateCommerceContext())
        {
            var saved = await commerce.Orders.SingleAsync(row => row.Id == order.Id);
            // COD có trạng thái chờ tiền bất thường vẫn phải bị loại bởi phương thức thanh toán.
            saved.Status = cod ? OrderStatus.PendingPayment : OrderStatus.PendingConfirmation;
            saved.PaymentStatus = cod ? PaymentStatus.Unpaid : PaymentStatus.Paid;
            await commerce.SaveChangesAsync();
        }
        var host = _kit.CreateHost();
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ExpiredPaymentProcessor>().ProcessAsync(CancellationToken.None);
        var result = await _kit.ReadOrderAsync(order.Id);
        Assert.Equal(cod ? OrderStatus.PendingPayment : OrderStatus.PendingConfirmation, result.Status);
        Assert.Equal(cod ? PaymentStatus.Unpaid : PaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal(8, await _kit.StockAsync(order.SkuId));
        Assert.Empty(_kit.Events.Observations);
    }

    [Fact]
    public async Task CommitFailure_RollsBackStockAndContinuesWithNextOrder()
    {
        var failed = await _kit.CreateOrderAsync(deadline: PaymentExpiryTestKit.Now.UtcDateTime.AddSeconds(-2));
        var succeeds = await _kit.CreateOrderAsync();
        var probe = new ExpiryCommitFailure(failed.Id);
        var host = _kit.CreateHost(services => services.AddDbContext<CommerceDbContext>(options => options.AddInterceptors(probe)));
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ExpiredPaymentProcessor>().ProcessAsync(CancellationToken.None);
        var unchanged = await _kit.ReadOrderAsync(failed.Id);
        Assert.Equal(OrderStatus.PendingPayment, unchanged.Status);
        Assert.DoesNotContain("payment_expired", unchanged.Flags);
        Assert.Single(unchanged.StatusHistory);
        Assert.Equal(8, await _kit.StockAsync(failed.SkuId));
        Assert.Equal(OrderStatus.Cancelled, (await _kit.ReadOrderAsync(succeeds.Id)).Status);
        Assert.Equal(10, await _kit.StockAsync(succeeds.SkuId));
        Assert.Equal(succeeds.Id, Assert.Single(_kit.Events.Observations).OrderId);
        Assert.Contains(factory.Logs.Entries, row => row.Message.Contains(failed.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebhookPaysAfterSelection_JobSkipsPaidOrder()
    {
        var order = await _kit.CreateOrderAsync();
        var probe = new PayAfterSelectionProbe();
        var host = _kit.CreateHost(services =>
            services.AddDbContext<CommerceDbContext>(options => options.AddInterceptors(probe)));
        using var client = host.CreateClient();
        using var scope = host.Services.CreateScope();
        // Giới hạn interceptor ở context của job để webhook không tự gọi lại chính nó.
        probe.ProcessorContextId = scope.ServiceProvider.GetRequiredService<CommerceDbContext>().ContextId.InstanceId;
        probe.PayAsync = () => PaymentExpiryTestKit.SendAsync(client, _kit.Payload(order));
        // Webhook commit sau truy vấn chọn đơn nhưng trước khóa, buộc job kiểm lại trạng thái.
        await scope.ServiceProvider.GetRequiredService<ExpiredPaymentProcessor>().ProcessAsync(CancellationToken.None);
        await AssertPaidOrderWasNotExpiredAsync(order);
    }

    [Fact]
    public async Task WebhookHoldsRowLock_JobWaitsThenSkipsPaidOrder()
    {
        var order = await _kit.CreateOrderAsync();
        var probe = new HoldPaidCommitProbe(order.Id);
        var host = _kit.CreateHost(services =>
            services.AddDbContext<CommerceDbContext>(options => options.AddInterceptors(probe)));
        using var client = host.CreateClient();
        using var scope = host.Services.CreateScope();
        // Giữ commit khớp để webhook còn sở hữu khóa dòng khi job chọn đơn quá hạn.
        var webhook = PaymentExpiryTestKit.SendAsync(client, _kit.Payload(order));
        Task processor = Task.CompletedTask;
        try
        {
            await probe.HoldingLock.Task.WaitAsync(TimeSpan.FromSeconds(10));
            processor = scope.ServiceProvider.GetRequiredService<ExpiredPaymentProcessor>().ProcessAsync(CancellationToken.None);
            // Quan sát PostgreSQL chứng minh job đang tranh khóa thật, không dựa vào thời gian ngủ.
            await LockWait.UntilSessionIsBlockedAsync(database);
        }
        finally
        {
            // Luôn giải phóng webhook để lỗi assertion không giữ khóa hoặc rò request sang test khác.
            probe.AllowCommit.TrySetResult();
            await Task.WhenAll(webhook, processor).WaitAsync(TimeSpan.FromSeconds(30));
        }
        await AssertPaidOrderWasNotExpiredAsync(order);
    }

    private async Task AssertPaidOrderWasNotExpiredAsync(PaymentExpiryTestKit.ExpiryOrder order)
    {
        // Đọc qua context riêng để kiểm trạng thái đã commit, không dùng entity trong interceptor.
        var saved = await _kit.ReadOrderAsync(order.Id);
        Assert.Equal(OrderStatus.PendingConfirmation, saved.Status);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Equal(8, await _kit.StockAsync(order.SkuId));
        Assert.DoesNotContain("payment_expired", saved.Flags);
        Assert.DoesNotContain(saved.StatusHistory, row => row.Reason == "Quá hạn thanh toán.");
        Assert.DoesNotContain(_kit.Events.Observations,
            row => row.OrderId == order.Id && !row.PaidEvent && row.Status == OrderStatus.Cancelled);
        // Lỗi chuyển trạng thái cũng là thất bại dù rollback giữ nguyên đơn đã trả tiền.
        Assert.DoesNotContain(factory.Logs.Entries,
            row => row.Message.Contains(order.Id, StringComparison.Ordinal)
                && row.Message.Contains("Hủy đơn hết hạn", StringComparison.Ordinal)
                && row.Message.Contains("thất bại", StringComparison.Ordinal));
    }

    private sealed class PayAfterSelectionProbe : DbCommandInterceptor
    {
        private int _paid;
        public Guid ProcessorContextId { get; set; }
        public Func<Task> PayAsync { get; set; } = null!;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            // Kết quả SELECT giữ ảnh chụp đơn chờ tiền; webhook commit trước khi job đọc lại dưới khóa.
            if (eventData.Context?.ContextId.InstanceId == ProcessorContextId
                && command.CommandText.Contains("FROM orders", StringComparison.Ordinal)
                && command.CommandText.Contains("LIMIT", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _paid, 1) == 0)
            {
                await PayAsync();
            }
            return result;
        }
    }

    private sealed class HoldPaidCommitProbe(string orderId) : DbTransactionInterceptor
    {
        public TaskCompletionSource HoldingLock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            // Chỉ chặn commit đổi đơn sang paid, không chặn transaction lưu thô của webhook.
            if (eventData.Context?.ChangeTracker.Entries<Order>()
                .Any(row => row.Entity.Id == orderId && row.Entity.PaymentStatus == PaymentStatus.Paid) == true)
            {
                HoldingLock.TrySetResult();
                await AllowCommit.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    // Lỗi tại commit kiểm rollback thật của cả hai module, không giả lỗi trước lời gọi kho.
    private sealed class ExpiryCommitFailure(string orderId) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Order>().Any(row => row.Entity.Id == orderId) == true)
            {
                throw new InvalidOperationException("Lỗi commit hết hạn được tạo bởi test.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
