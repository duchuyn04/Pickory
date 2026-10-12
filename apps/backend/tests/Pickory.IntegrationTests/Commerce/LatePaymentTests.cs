using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Payments;
using Pickory.Modules.Commerce.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class LatePaymentTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private readonly PaymentExpiryTestKit _kit = new(factory, database);
    public Task InitializeAsync() => _kit.InitializeAsync();
    public async Task DisposeAsync() => await _kit.DisposeAsync();

    private async Task ExpireAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ExpiredPaymentProcessor>().ProcessAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task FullLatePayment_ReReservesAndEmitsBothEventsAfterCommit(int extra)
    {
        var order = await _kit.CreateOrderAsync();
        var host = _kit.CreateHost();
        await ExpireAsync(host.Services);
        // Chỉ đo sự kiện khôi phục, không đếm sự kiện hủy đã kiểm ở test processor.
        _kit.Events.Observations.Clear();
        await PaymentExpiryTestKit.SendAsync(host.CreateClient(), _kit.Payload(order, order.Total + extra));
        var saved = await _kit.ReadOrderAsync(order.Id);
        Assert.Equal(OrderStatus.PendingConfirmation, saved.Status);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Equal(8, await _kit.StockAsync(order.SkuId));
        Assert.DoesNotContain("payment_expired", saved.Flags);
        Assert.Contains("late_payment", saved.Flags);
        Assert.Equal(extra > 0, saved.Flags.Contains("overpaid"));
        Assert.Equal(1, saved.StatusHistory.Count(row => row.FromStatus == OrderStatus.Cancelled && row.ToStatus == OrderStatus.PendingConfirmation));
        var observations = _kit.Events.Observations.ToArray();
        Assert.Equal(2, observations.Length);
        Assert.Single(observations, row => row.PaidEvent);
        Assert.Single(observations, row => !row.PaidEvent);
        Assert.All(observations, row =>
        {
            Assert.Equal(OrderStatus.PendingConfirmation, row.Status);
            Assert.Equal(PaymentStatus.Paid, row.PaymentStatus);
            Assert.Equal(8, row.Stock);
            Assert.Equal(3, row.HistoryCount);
        });
        await using var commerce = database.CreateCommerceContext();
        var payment = await commerce.PaymentTransactions.SingleAsync(row => row.OrderId == order.Id);
        Assert.Equal("matched", payment.MatchStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShortStockOrUnderpayment_RequiresRefundWithoutRestoringOrder(bool shortStock)
    {
        var order = await _kit.CreateOrderAsync();
        var host = _kit.CreateHost();
        await ExpireAsync(host.Services);
        if (shortStock)
        {
            // Kho hết sau khi hủy mô phỏng hàng đã bán cho đơn khác, không cho giữ số lượng âm.
            await using var catalog = database.CreateCatalogContext();
            await catalog.Skus.Where(row => row.Id == order.SkuId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Stock, 0));
        }
        _kit.Events.Observations.Clear();
        await PaymentExpiryTestKit.SendAsync(host.CreateClient(), _kit.Payload(order, shortStock ? order.Total : order.Total - 1));
        var saved = await _kit.ReadOrderAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, saved.Status);
        Assert.Equal(PaymentStatus.RefundPending, saved.PaymentStatus);
        Assert.Equal(shortStock ? 0 : 10, await _kit.StockAsync(order.SkuId));
        Assert.Contains("payment_expired", saved.Flags);
        Assert.Equal(!shortStock, saved.Flags.Contains("underpaid"));
        Assert.DoesNotContain("late_payment", saved.Flags);
        Assert.Equal(2, saved.StatusHistory.Count);
        Assert.Empty(_kit.Events.Observations);
        await using var commerce = database.CreateCommerceContext();
        Assert.Equal("matched", (await commerce.PaymentTransactions.SingleAsync(row => row.OrderId == order.Id)).MatchStatus);
    }

    [Fact]
    public async Task CustomerCancelledOrder_IsNotRestored()
    {
        var order = await _kit.CreateOrderAsync();
        await _kit.CancelAsync(order);
        var host = _kit.CreateHost();
        await PaymentExpiryTestKit.SendAsync(host.CreateClient(), _kit.Payload(order));
        var saved = await _kit.ReadOrderAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, saved.Status);
        Assert.Equal(PaymentStatus.Unpaid, saved.PaymentStatus);
        Assert.Equal(10, await _kit.StockAsync(order.SkuId));
        Assert.DoesNotContain("late_payment", saved.Flags);
        Assert.Empty(_kit.Events.Observations);
        await using var commerce = database.CreateCommerceContext();
        var payment = await commerce.PaymentTransactions.SingleAsync();
        Assert.Equal("unmatched", payment.MatchStatus);
        Assert.Null(payment.OrderId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameLateTransactionThreeTimes_RestoresOnce(bool concurrent)
    {
        var order = await _kit.CreateOrderAsync();
        var probe = new SepayWebhookTransactionProbe(database, new SepayPaidEventObserver(database));
        var host = _kit.CreateHost(services => services.AddDbContext<CommerceDbContext>(options => options.AddInterceptors(probe)));
        await ExpireAsync(host.Services);
        _kit.Events.Observations.Clear();
        var payload = _kit.Payload(order);
        // Hàng rào buộc request trùng cùng vượt truy vấn ID trước khi tranh unique chứng từ.
        probe.Arm(rawParticipants: concurrent ? 3 : 0);
        var client = host.CreateClient();
        if (concurrent)
        {
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PaymentExpiryTestKit.SendAsync(client, payload)));
        }
        else
        {
            for (var index = 0; index < 3; index++) await PaymentExpiryTestKit.SendAsync(client, payload);
        }
        var saved = await _kit.ReadOrderAsync(order.Id);
        Assert.Equal(OrderStatus.PendingConfirmation, saved.Status);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Equal(8, await _kit.StockAsync(order.SkuId));
        Assert.Equal(3, saved.StatusHistory.Count);
        Assert.Equal(2, _kit.Events.Observations.Count);
        await using var commerce = database.CreateCommerceContext();
        Assert.Equal(1, await commerce.PaymentTransactions.CountAsync(row => row.OrderId == order.Id));
    }

    [Fact]
    public async Task RestoreThenCustomerCancel_ReturnsStockToOriginalQuantity()
    {
        var order = await _kit.CreateOrderAsync();
        var host = _kit.CreateHost();
        await ExpireAsync(host.Services);
        await PaymentExpiryTestKit.SendAsync(host.CreateClient(), _kit.Payload(order));
        Assert.Equal(8, await _kit.StockAsync(order.SkuId));
        await _kit.CancelAsync(order);
        var saved = await _kit.ReadOrderAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, saved.Status);
        Assert.Equal(PaymentStatus.RefundPending, saved.PaymentStatus);
        Assert.Equal(10, await _kit.StockAsync(order.SkuId));
        Assert.Equal(4, saved.StatusHistory.Count);
    }

    [Fact]
    public async Task LateMatchingCommitFailure_RollsBackReReservationAndKeepsRawReceipt()
    {
        var order = await _kit.CreateOrderAsync();
        var probe = new SepayWebhookTransactionProbe(database, new SepayPaidEventObserver(database));
        var host = _kit.CreateHost(services => services.AddDbContext<CommerceDbContext>(options => options.AddInterceptors(probe)));
        await ExpireAsync(host.Services);
        _kit.Events.Observations.Clear();
        // Lỗi commit xảy ra sau khi Catalog đã trừ kho, buộc hai module rollback cùng nhau.
        probe.Arm(failMatchingCommit: true);
        var payload = _kit.Payload(order);
        await PaymentExpiryTestKit.SendAsync(host.CreateClient(), payload);
        var saved = await _kit.ReadOrderAsync(order.Id);
        Assert.Equal(OrderStatus.Cancelled, saved.Status);
        Assert.Equal(PaymentStatus.Unpaid, saved.PaymentStatus);
        Assert.Contains("payment_expired", saved.Flags);
        Assert.DoesNotContain("late_payment", saved.Flags);
        Assert.Equal(2, saved.StatusHistory.Count);
        Assert.Equal(10, await _kit.StockAsync(order.SkuId));
        Assert.Empty(_kit.Events.Observations);
        await using var commerce = database.CreateCommerceContext();
        var payment = await commerce.PaymentTransactions.SingleAsync();
        Assert.Equal("unmatched", payment.MatchStatus);
        Assert.Null(payment.OrderId);
        // Gửi lại chứng từ thất bại không được tự khớp lại; nhân viên phải đối soát tay.
        await PaymentExpiryTestKit.SendAsync(host.CreateClient(), payload);
        Assert.Equal(1, probe.MatchingCommitAttempts);
        Assert.Equal(10, await _kit.StockAsync(order.SkuId));
    }
}
