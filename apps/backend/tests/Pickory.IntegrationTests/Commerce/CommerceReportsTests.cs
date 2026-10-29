using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Contracts.Reports;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Payments;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-23: kiểm tra `ICommerceReports` qua DI container thật và DB thật.

// Bộ dữ liệu dựng sẵn và bảng tính tay nằm trong khối bên dưới.
/*
Bộ dữ liệu dựng sẵn gồm 19 đơn trên 3 ngày giờ Việt Nam: D1 = 10/3/2040, D2 = 11/3/2040, D3 = 12/3/2040.
Khoảng báo cáo là D1 đến D3. D0 = 9/3/2040 và D4 = 13/3/2040 nằm ngoài khoảng.
Mọi giờ trong bảng là giờ Việt Nam (UTC+7). Tổng tiền đơn = tổng dòng hàng + 30.000 phí ship - giảm phí ship.

Mã  Phương thức/Trạng thái/Thanh toán         Tạo        Ngày thanh toán          Tổng      Giảm ship
O01 Sepay  Confirmed            Paid          D1 09:00   D1 09:05 (giao dịch)    1.010.000  20.000
O02 Sepay  PendingConfirmation  Paid          D1 23:59:59 D2 00:00:00 (giao dịch)   480.000      0   (giao dịch thiếu D1 23:30 không đổi ngày)
O03 COD    Delivered            Paid          D1 10:00   D2 23:59:59 (nhận COD)     320.000  10.000
O04 COD    Delivered            Paid          D2 08:00   D3 00:00:00 (nhận COD)     230.000      0
O05 COD    Delivered            Unpaid        D2 11:00   -                          470.000  10.000
O06 Sepay  Cancelled + hết hạn  RefundPending D2 12:00   -                          480.000      0
O07 Sepay  Cancelled (shop)     Refunded      D2 14:00   -                          230.000      0
O08 COD    Cancelled (khách)    Unpaid        D3 09:00   -                          220.000  10.000
O09 Sepay  Cancelled + hết hạn  Unpaid        D3 10:00   -                          465.000  15.000
O10 Sepay  PendingConfirmation  Paid          D1 15:00   D3 08:00 (trả muộn)      130.000      0   (đã khôi phục, mất cờ hết hạn)
O11 COD    Shipping             Unpaid        D3 14:00   -                          430.000      0
O12 Sepay  Confirmed            Paid          D0 22:00   D1 00:30 (giao dịch)      480.000      0
O13 Sepay  Confirmed            Paid          D3 23:00   D4 00:00:00 (ngoài khoảng) 480.000      0
O14 COD    PendingConfirmation  Unpaid        D4 00:00:00 (ngoài khoảng)            230.000      0
O15 COD    PendingConfirmation  Unpaid        D1 00:00:00 (trong khoảng)            230.000      0
O16 COD    Delivered            Paid          D2 09:00   D4 00:00:00 (ngoài khoảng) 230.000      0
O17 Sepay  Confirmed            Paid          D2 16:00   không có giao dịch đủ tiền 130.000      0   (chỉ có giao dịch thiếu 100.000)
O18 Sepay  Delivered            Unpaid        D2 17:00   -                          130.000      0
O19 Sepay  Confirmed            Paid          D3 11:00   D3 11:05 (giao dịch)       730.000      0

Bảng tính tay theo ngày. Giá trị đơn đặt theo ngày tạo, doanh thu và phương thức theo ngày thanh toán:

D1: tạo O01 O02 O03 O10 O15 = 5 đơn, không đơn hủy.
    Giá trị đơn đặt = 1.010.000 + 480.000 + 320.000 + 130.000 + 230.000 = 2.170.000.
    Giảm ship = 20.000 + 10.000 = 30.000.
    Phương thức đã thanh toán: COD = 0, Sepay (O01 O12) = 2.
    Doanh thu = O01 1.010.000 + O12 480.000 = 1.490.000 trên 2 đơn.
D2: tạo O04 O05 O06 O07 O16 O17 O18 = 7 đơn. Hủy: O07 shop = 1, O06 hết hạn = 1.
    Giá trị đơn đặt bỏ O06 = 230.000 + 470.000 + 230.000 + 230.000 + 130.000 + 130.000 = 1.420.000.
    Giảm ship = O05 10.000.
    Phương thức đã thanh toán: COD (O03) = 1, Sepay (O02) = 1.
    Doanh thu = O02 480.000 + O03 320.000 = 800.000 trên 2 đơn. O06 và O07 không tính vì RefundPending và Refunded.
D3: tạo O08 O09 O11 O13 O19 = 5 đơn. Hủy: O08 khách = 1, O09 hết hạn = 1.
    Giá trị đơn đặt bỏ O09 = 220.000 + 430.000 + 480.000 + 730.000 = 1.860.000.
    Giảm ship = O08 10.000.
    Phương thức đã thanh toán: COD (O04) = 1, Sepay (O10 O19) = 2.
    Doanh thu = O04 230.000 + O10 130.000 + O19 730.000 = 1.090.000 trên 3 đơn.

Bán theo sản phẩm (đơn Paid có ngày thanh toán trong khoảng: O01 O02 O03 O04 O10 O12 O19):
    prod-p1: O01 2x450.000 + O02 1x450.000 + O12 1x450.000 = 4 sản phẩm, 1.800.000.
    prod-p2: O01 1x100.000 + O03 3x100.000 + O10 1x100.000 = 5 sản phẩm, 500.000.
    prod-p4: O19 2x250.000 = 2 sản phẩm, 500.000 (hòa doanh thu với prod-p2, ít sản phẩm hơn nên xếp sau).
    prod-Z và prod-a: cùng 1 sản phẩm, 200.000 (prod-Z từ O19, prod-a từ O04). Xếp theo ordinal nên 'Z' trước 'a'.

Việc chờ: PendingConfirmation = O02 O10 O14 O15 = 4. Giao dịch chưa khớp = 2. COD đã giao chưa nhận tiền = O05 = 1.

Cách giữ test ổn định: dữ liệu dùng mốc thời gian cố định ở năm 2040 đến 2042 nên không phụ thuộc giờ máy.
Mỗi test xóa toàn bộ đơn và giao dịch trước và sau khi chạy, nên số liệu việc chờ không lẫn dữ liệu của test khác.
*/
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CommerceReportsTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string AuditTargetType = "order";
    private const string ProductP1 = "prod-p1";
    private const string ProductP2 = "prod-p2";
    private const string ProductP4 = "prod-p4";
    private const string ProductUpperZ = "prod-Z";
    private const string ProductLowerA = "prod-a";
    private const string ExpiredReason = "Quá hạn thanh toán.";

    private static readonly DateRange FixtureRange = new(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 12));

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly AdminOrderTestKit _resetKit;
    private readonly List<Order> _orders = [];
    private readonly List<PaymentTransaction> _transactions = [];
    private readonly List<OrderStatusHistory> _histories = [];
    private readonly List<string> _auditedCodes = [];

    public CommerceReportsTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _resetKit = new AdminOrderTestKit(database);
    }

    public Task InitializeAsync() => _resetKit.ResetAsync();

    public async Task DisposeAsync()
    {
        await using (var identity = _database.CreateIdentityContext())
        {
            await identity.AuditLogs
                .Where(log => log.TargetType == AuditTargetType && _auditedCodes.Contains(log.TargetId))
                .ExecuteDeleteAsync();
        }

        await _resetKit.ResetAsync();
    }

    // ---------- Dựng dữ liệu ----------

    // Giờ Việt Nam (UTC+7) của ngày trong tháng 3/2040, đổi sang UTC. Cùng offset cố định với `VietnamCalendar`.
    private static DateTime Vn(int day, int hour, int minute = 0, int second = 0) =>
        DateTime.SpecifyKind(new DateTime(2040, 3, day, hour, minute, second).AddHours(-7), DateTimeKind.Utc);

    private static JsonDocument Json(object value) => JsonSerializer.SerializeToDocument(value);

    private Order AddOrder(
        string code,
        PaymentMethod method,
        OrderStatus status,
        PaymentStatus paymentStatus,
        DateTime createdAtUtc,
        int shippingDiscount,
        string[] flags,
        params (string ProductId, int Quantity, int UnitPrice)[] lines)
    {
        var orderId = "report-order-" + code;
        var subtotal = lines.Sum(line => line.Quantity * line.UnitPrice);
        var order = new Order
        {
            Id = orderId,
            Code = code,
            AnonymousId = "anon-" + code,
            Contact = Json(new { name = "Nguyễn Văn An", phone = "0901234567", email = "an@example.com" }),
            ShippingAddress = Json(new { provinceCode = "1000001", wardCode = "1003646", line = "12 Phố Test" }),
            PaymentMethod = method,
            Status = status,
            PaymentStatus = paymentStatus,
            Flags = flags.ToList(),
            Subtotal = subtotal,
            ShippingFee = 30_000,
            ShippingDiscount = shippingDiscount,
            Total = subtotal + 30_000 - shippingDiscount,
            AccessTokenHash = "hash-" + code,
            IdempotencyKey = "key-" + code,
            CreatedAt = createdAtUtc,
        };

        for (var index = 0; index < lines.Length; index++)
        {
            order.Items.Add(new OrderItem
            {
                Id = $"{orderId}-line-{index}",
                OrderId = orderId,
                SkuId = $"sku-{code}-{index}",
                ProductId = lines[index].ProductId,
                NameSnapshot = "Sản phẩm " + lines[index].ProductId,
                SkuCodeSnapshot = $"SKU-{code}-{index}",
                OptionValues = Json(new { Màu = "Đỏ" }),
                UnitPrice = lines[index].UnitPrice,
                Quantity = lines[index].Quantity,
            });
        }

        _orders.Add(order);
        return order;
    }

    private void AddTransaction(Order? order, string matchStatus, int amount, DateTime receivedAtUtc)
    {
        _transactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid().ToString(),
            Provider = "sepay",
            ProviderTxId = Guid.NewGuid().ToString("N"),
            Amount = amount,
            Content = order?.Code ?? "khong khop don",
            Raw = Json(new { }),
            OrderId = order?.Id,
            MatchStatus = matchStatus,
            ReceivedAt = receivedAtUtc,
        });
    }

    private void AddHistory(
        Order order, OrderStatus from, OrderStatus to, string reason, DateTime createdAtUtc, string? id = null)
    {
        _histories.Add(new OrderStatusHistory
        {
            Id = id ?? Guid.NewGuid().ToString(),
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            CreatedAt = createdAtUtc,
        });
    }

    private async Task SaveSeedAsync()
    {
        await using var commerce = _database.CreateCommerceContext();
        commerce.Orders.AddRange(_orders);
        commerce.PaymentTransactions.AddRange(_transactions);
        commerce.OrderStatusHistory.AddRange(_histories);
        await commerce.SaveChangesAsync();
    }

    // Dựng đúng bộ 19 đơn trong bảng tính tay ở đầu file.
    private async Task SeedFixtureAsync()
    {
        var o01 = AddOrder("O01", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(10, 9), 20_000, [],
            (ProductP1, 2, 450_000), (ProductP2, 1, 100_000));
        AddTransaction(o01, "matched", 1_010_000, Vn(10, 9, 5));

        var o02 = AddOrder("O02", PaymentMethod.Sepay, OrderStatus.PendingConfirmation, PaymentStatus.Paid, Vn(10, 23, 59, 59), 0,
            ["underpaid"], (ProductP1, 1, 450_000));
        AddTransaction(o02, "matched", 100_000, Vn(10, 23, 30));
        AddTransaction(o02, "matched", 480_000, Vn(11, 0, 0));

        var o03 = AddOrder("O03", PaymentMethod.Cod, OrderStatus.Delivered, PaymentStatus.Paid, Vn(10, 10), 10_000, [],
            (ProductP2, 3, 100_000));
        AddHistory(o03, OrderStatus.Delivered, OrderStatus.Delivered, "Đã nhận tiền COD từ GHN.", Vn(11, 23, 59, 59));

        var o04 = AddOrder("O04", PaymentMethod.Cod, OrderStatus.Delivered, PaymentStatus.Paid, Vn(11, 8), 0, [],
            (ProductLowerA, 1, 200_000));
        AddHistory(o04, OrderStatus.Delivered, OrderStatus.Delivered, "Đã nhận tiền COD từ GHN.", Vn(12, 0, 0));

        AddOrder("O05", PaymentMethod.Cod, OrderStatus.Delivered, PaymentStatus.Unpaid, Vn(11, 11), 10_000, [],
            (ProductP1, 1, 450_000));

        var o06 = AddOrder("O06", PaymentMethod.Sepay, OrderStatus.Cancelled, PaymentStatus.RefundPending, Vn(11, 12), 0,
            ["payment_expired"], (ProductP1, 1, 450_000));
        AddTransaction(o06, "matched", 480_000, Vn(11, 13));
        AddHistory(o06, OrderStatus.PendingPayment, OrderStatus.Cancelled, ExpiredReason, Vn(11, 12, 30));

        var o07 = AddOrder("O07", PaymentMethod.Sepay, OrderStatus.Cancelled, PaymentStatus.Refunded, Vn(11, 14), 0, [],
            (ProductP2, 2, 100_000));
        AddTransaction(o07, "matched", 230_000, Vn(11, 14, 5));
        AddHistory(o07, OrderStatus.PendingConfirmation, OrderStatus.Cancelled, "Hết hàng.", Vn(11, 15));

        var o08 = AddOrder("O08", PaymentMethod.Cod, OrderStatus.Cancelled, PaymentStatus.Unpaid, Vn(12, 9), 10_000, [],
            (ProductLowerA, 1, 200_000));
        AddHistory(o08, OrderStatus.PendingConfirmation, OrderStatus.Cancelled, "Khách tự hủy.", Vn(12, 9, 30));

        var o09 = AddOrder("O09", PaymentMethod.Sepay, OrderStatus.Cancelled, PaymentStatus.Unpaid, Vn(12, 10), 15_000,
            ["payment_expired"], (ProductP1, 1, 450_000));
        AddHistory(o09, OrderStatus.PendingPayment, OrderStatus.Cancelled, ExpiredReason, Vn(12, 10, 30));

        var o10 = AddOrder("O10", PaymentMethod.Sepay, OrderStatus.PendingConfirmation, PaymentStatus.Paid, Vn(10, 15), 0,
            ["late_payment"], (ProductP2, 1, 100_000));
        AddTransaction(o10, "matched", 130_000, Vn(12, 8));
        AddHistory(o10, OrderStatus.PendingPayment, OrderStatus.Cancelled, ExpiredReason, Vn(10, 15, 30));
        AddHistory(o10, OrderStatus.Cancelled, OrderStatus.PendingConfirmation, "SePay nhận đủ tiền sau khi hết hạn.", Vn(12, 8));

        AddOrder("O11", PaymentMethod.Cod, OrderStatus.Shipping, PaymentStatus.Unpaid, Vn(12, 14), 0, [],
            (ProductLowerA, 2, 200_000));

        var o12 = AddOrder("O12", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(9, 22), 0, [],
            (ProductP1, 1, 450_000));
        AddTransaction(o12, "matched", 480_000, Vn(10, 0, 30));

        var o13 = AddOrder("O13", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(12, 23), 0, [],
            (ProductP1, 1, 450_000));
        AddTransaction(o13, "matched", 480_000, Vn(13, 0, 0));

        AddOrder("O14", PaymentMethod.Cod, OrderStatus.PendingConfirmation, PaymentStatus.Unpaid, Vn(13, 0, 0), 0, [],
            (ProductLowerA, 1, 200_000));

        AddOrder("O15", PaymentMethod.Cod, OrderStatus.PendingConfirmation, PaymentStatus.Unpaid, Vn(10, 0, 0), 0, [],
            (ProductLowerA, 1, 200_000));

        var o16 = AddOrder("O16", PaymentMethod.Cod, OrderStatus.Delivered, PaymentStatus.Paid, Vn(11, 9), 0, [],
            (ProductLowerA, 1, 200_000));
        AddHistory(o16, OrderStatus.Delivered, OrderStatus.Delivered, "Đã nhận tiền COD từ GHN.", Vn(13, 0, 0));

        var o17 = AddOrder("O17", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(11, 16), 0, [],
            (ProductP2, 1, 100_000));
        AddTransaction(o17, "matched", 100_000, Vn(11, 16, 5));

        AddOrder("O18", PaymentMethod.Sepay, OrderStatus.Delivered, PaymentStatus.Unpaid, Vn(11, 17), 0, [],
            (ProductP2, 1, 100_000));

        var o19 = AddOrder("O19", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(12, 11), 0, [],
            (ProductP4, 2, 250_000), (ProductUpperZ, 1, 200_000));
        AddTransaction(o19, "matched", 730_000, Vn(12, 11, 5));

        // Hai giao dịch chưa khớp và một giao dịch bị bỏ qua. Chỉ loại `unmatched` được đếm.
        AddTransaction(null, "unmatched", 50_000, Vn(11, 10));
        AddTransaction(null, "unmatched", 70_000, Vn(12, 10));
        AddTransaction(null, "ignored", 10_000, Vn(12, 12));

        await SaveSeedAsync();
    }

    private async Task<T> WithReportsAsync<T>(Func<ICommerceReports, Task<T>> action)
    {
        // Mở scope dịch vụ độc lập như môi trường request thật để kiểm tra đăng ký DI.
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICommerceReports>());
    }

    private static DailySales Day(
        int dayOfMonth, long revenue, int paidCount, long placedValue, int createdCount,
        CancellationCounts cancellations, PaymentMethodCounts methods, long shippingDiscount,
        int soldQuantity = 0, OrderStatusCounts? orderStatuses = null) =>
        new(
            new DateOnly(2040, 3, dayOfMonth), revenue, paidCount, placedValue, createdCount, cancellations, methods,
            shippingDiscount, soldQuantity, orderStatuses ?? new OrderStatusCounts(0, 0, 0, 0, 0, 0, 0));

    // ---------- GetDailySalesAsync ----------

    [Fact]
    // Bộ dữ liệu dựng sẵn cho ra đúng từng số của bảng tính tay ở đầu file, ở cả ba ngày.
    public async Task GetDailySales_Fixture_MatchesHandCalculatedTable()
    {
        await SeedFixtureAsync();

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(FixtureRange, CancellationToken.None));

        Assert.Equal(
            [
                Day(10, 1_490_000, 2, 2_170_000, 5, new CancellationCounts(0, 0, 0), new PaymentMethodCounts(0, 2), 30_000, 4,
                    new OrderStatusCounts(0, 3, 1, 0, 1, 0, 0)),
                Day(11, 800_000, 2, 1_420_000, 7, new CancellationCounts(0, 1, 1), new PaymentMethodCounts(1, 1), 10_000, 4,
                    new OrderStatusCounts(0, 0, 1, 0, 4, 0, 2)),
                Day(12, 1_090_000, 3, 1_860_000, 5, new CancellationCounts(1, 0, 1), new PaymentMethodCounts(1, 2), 10_000, 5,
                    new OrderStatusCounts(0, 0, 2, 1, 0, 0, 2)),
            ],
            days);
    }

    [Fact]
    // Khoảng một ngày chỉ trả dòng của ngày đó. Đơn của các ngày khác không lọt vào.
    public async Task GetDailySales_SingleDayRange_ReturnsOnlyThatDay()
    {
        await SeedFixtureAsync();
        var onlyD2 = new DateRange(new DateOnly(2040, 3, 11), new DateOnly(2040, 3, 11));

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(onlyD2, CancellationToken.None));

        Assert.Equal(
            [Day(11, 800_000, 2, 1_420_000, 7, new CancellationCounts(0, 1, 1), new PaymentMethodCounts(1, 1), 10_000, 4,
                new OrderStatusCounts(0, 0, 1, 0, 4, 0, 2))],
            days);
    }

    [Fact]
    // Khoảng không có dữ liệu vẫn trả một dòng toàn số 0 cho mỗi ngày.
    public async Task GetDailySales_RangeWithoutData_ReturnsZeroRowPerDay()
    {
        var range = new DateRange(new DateOnly(2043, 1, 1), new DateOnly(2043, 1, 3));

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(range, CancellationToken.None));

        Assert.Equal(3, days.Count);
        Assert.All(days, day => Assert.Equal(
            new DailySales(
                day.Day, 0, 0, 0, 0, new CancellationCounts(0, 0, 0), new PaymentMethodCounts(0, 0), 0, 0,
                new OrderStatusCounts(0, 0, 0, 0, 0, 0, 0)), day));
    }

    [Fact]
    // Biên nửa đêm giờ Việt Nam: 16:59:59 UTC thuộc ngày D, 17:00:00 UTC thuộc ngày D+1.
    // Kiểm cho ngày tạo đơn, thời điểm giao dịch SePay và thời điểm nhận tiền COD.
    public async Task GetDailySales_VietnamMidnightBoundary_SplitsCreatedSepayAndCodRowsAcrossDays()
    {
        // Khoảng báo cáo là 1/5/2041 đến 2/5/2041. Bốn mốc UTC lần lượt thuộc 30/4 (ngoài khoảng), 1/5, 1/5 và 2/5.
        DateTime[] instants =
        [
            new(2041, 4, 30, 16, 59, 59, DateTimeKind.Utc),
            new(2041, 4, 30, 17, 0, 0, DateTimeKind.Utc),
            new(2041, 5, 1, 16, 59, 59, DateTimeKind.Utc),
            new(2041, 5, 1, 17, 0, 0, DateTimeKind.Utc),
        ];
        for (var index = 0; index < instants.Length; index++)
        {
            // Mỗi mốc có một đơn Sepay tạo và trả tiền đúng mốc đó, và một đơn COD tạo và nhận tiền đúng mốc đó.
            var sepay = AddOrder($"BS{index}", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, instants[index], 0, [],
                (ProductP1, 1, 70_000));
            AddTransaction(sepay, "matched", sepay.Total, instants[index]);

            var cod = AddOrder($"BC{index}", PaymentMethod.Cod, OrderStatus.Delivered, PaymentStatus.Paid, instants[index], 0, [],
                (ProductP2, 1, 70_000));
            AddHistory(cod, OrderStatus.Delivered, OrderStatus.Delivered, "Đã nhận tiền COD từ GHN.", instants[index]);
        }

        await SaveSeedAsync();
        var range = new DateRange(new DateOnly(2041, 5, 1), new DateOnly(2041, 5, 2));

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(range, CancellationToken.None));

        // Mỗi đơn có tổng 100.000 (70.000 hàng cộng 30.000 phí ship). Ngày 1/5 nhận hai mốc, ngày 2/5 nhận một mốc.
        Assert.Equal(new DateOnly(2041, 5, 1), days[0].Day);
        Assert.Equal(400_000, days[0].RecognizedRevenue);
        Assert.Equal(4, days[0].PaidOrderCount);
        Assert.Equal(400_000, days[0].PlacedOrderValue);
        Assert.Equal(4, days[0].CreatedOrderCount);
        Assert.Equal(new DateOnly(2041, 5, 2), days[1].Day);
        Assert.Equal(200_000, days[1].RecognizedRevenue);
        Assert.Equal(2, days[1].PaidOrderCount);
        Assert.Equal(200_000, days[1].PlacedOrderValue);
        Assert.Equal(2, days[1].CreatedOrderCount);
    }

    [Fact]
    // Đơn tạo ngày 1 nhưng trả tiền ngày 2: doanh thu vào ngày 2, giá trị đơn đặt vào ngày 1.
    public async Task GetDailySales_OrderCreatedOnDayOnePaidOnDayTwo_SplitsRevenueAndPlacedValue()
    {
        var order = AddOrder("SPLIT", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(10, 20), 0, [],
            (ProductP1, 1, 170_000));
        AddTransaction(order, "matched", order.Total, Vn(11, 9));
        await SaveSeedAsync();

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 11)), CancellationToken.None));

        Assert.Equal(0, days[0].RecognizedRevenue);
        Assert.Equal(200_000, days[0].PlacedOrderValue);
        Assert.Equal(new PaymentMethodCounts(Cod: 0, Sepay: 0), days[0].PaymentMethods);
        Assert.Equal(200_000, days[1].RecognizedRevenue);
        Assert.Equal(0, days[1].PlacedOrderValue);
        Assert.Equal(new PaymentMethodCounts(Cod: 0, Sepay: 1), days[1].PaymentMethods);
    }

    [Fact]
    // Giao dịch chuyển thiếu được gắn đơn không làm đơn thành `paid`, nên không đổi ngày thanh toán.
    // Ngày thanh toán là giao dịch đủ tiền đầu tiên, và đơn chỉ có giao dịch thiếu không có doanh thu.
    public async Task GetDailySales_UnderpaidTransaction_DoesNotChangePaymentDay()
    {
        var paidLater = AddOrder("UNDER1", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(10, 9), 0, [],
            (ProductP1, 1, 70_000));
        AddTransaction(paidLater, "matched", 40_000, Vn(10, 10));
        AddTransaction(paidLater, "matched", paidLater.Total, Vn(11, 10));

        var onlyUnderpaid = AddOrder("UNDER2", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(10, 9), 0, [],
            (ProductP1, 1, 70_000));
        AddTransaction(onlyUnderpaid, "matched", 40_000, Vn(10, 10));
        await SaveSeedAsync();

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 11)), CancellationToken.None));

        Assert.Equal(0, days[0].RecognizedRevenue);
        Assert.Equal(0, days[0].PaidOrderCount);
        Assert.Equal(100_000, days[1].RecognizedRevenue);
        Assert.Equal(1, days[1].PaidOrderCount);
    }

    [Fact]
    // Giao dịch `unmatched` hoặc `ignored` không gắn đơn nên không làm đơn có ngày thanh toán.
    public async Task GetDailySales_NonMatchedTransaction_DoesNotSetPaymentDay()
    {
        var order = AddOrder("NOMATCH", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(10, 9), 0, [],
            (ProductP1, 1, 70_000));
        AddTransaction(order, "unmatched", order.Total, Vn(10, 10));
        await SaveSeedAsync();

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 10)), CancellationToken.None));

        Assert.Equal(0, Assert.Single(days).RecognizedRevenue);
    }

    [Fact]
    // Đơn `Paid` không tìm được thời điểm thanh toán không tính vào doanh thu, và hàm ghi một log cảnh báo kèm số đơn.
    public async Task GetDailySales_PaidOrderWithoutPaymentTime_IsExcludedAndLogsWarning()
    {
        await SeedFixtureAsync();
        var warningsBefore = WarningsAboutMissingPaymentTime().Count;

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(FixtureRange, CancellationToken.None));

        // O17 là đơn duy nhất có `Paid` mà chỉ có giao dịch thiếu tiền. Doanh thu D2 vẫn là 800.000.
        Assert.Equal(800_000, days[1].RecognizedRevenue);
        var warnings = WarningsAboutMissingPaymentTime();
        Assert.Equal(warningsBefore + 1, warnings.Count);
        Assert.Contains("1 đơn", warnings[^1].Message, StringComparison.Ordinal);

        // Khoảng chỉ có ngày 12/3 không chứa ngày tạo O17 (11/3), nên không có cảnh báo mới.
        await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2040, 3, 12), new DateOnly(2040, 3, 12)), CancellationToken.None));
        Assert.Equal(warningsBefore + 1, WarningsAboutMissingPaymentTime().Count);

        // Hàm bán theo sản phẩm không ghi cảnh báo, kể cả khi khoảng chứa ngày tạo O17.
        await WithReportsAsync(reports => reports.GetProductSalesAsync(FixtureRange, CancellationToken.None));
        Assert.Equal(warningsBefore + 1, WarningsAboutMissingPaymentTime().Count);
    }

    [Fact]
    // Dòng `Delivered` sang `Delivered` còn do thao tác khác ghi, nên chỉ dòng có lý do nhận tiền COD mới tính là thanh toán.
    // Đơn có cả hai dòng lấy ngày của dòng nhận tiền.
    public async Task GetDailySales_CodPaymentDay_UsesOnlyCodReceiptHistory()
    {
        var withoutReceipt = AddOrder("COD1", PaymentMethod.Cod, OrderStatus.Delivered, PaymentStatus.Paid, Vn(10, 9), 0, [],
            (ProductP1, 1, 70_000));
        AddHistory(withoutReceipt, OrderStatus.Delivered, OrderStatus.Delivered, "Đã xuất hóa đơn VAT.", Vn(10, 10));

        var withBoth = AddOrder("COD2", PaymentMethod.Cod, OrderStatus.Delivered, PaymentStatus.Paid, Vn(10, 9), 0, [],
            (ProductP1, 1, 70_000));
        AddHistory(withBoth, OrderStatus.Delivered, OrderStatus.Delivered, "Đã xuất hóa đơn VAT.", Vn(10, 10));
        AddHistory(withBoth, OrderStatus.Delivered, OrderStatus.Delivered, "Đã nhận tiền COD từ GHN.", Vn(11, 10));
        await SaveSeedAsync();

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 11)), CancellationToken.None));

        Assert.Equal(0, days[0].RecognizedRevenue);
        Assert.Equal(100_000, days[1].RecognizedRevenue);
        Assert.Equal(1, days[1].PaidOrderCount);
    }

    [Fact]
    // Đơn mang cờ `payment_expired` được xếp vào quá hạn dù lịch sử hủy ghi lý do của khách. Cờ thắng lý do lịch sử.
    public async Task GetDailySales_ExpiredFlagWithCustomerHistoryReason_IsCountedAsPaymentExpired()
    {
        var order = AddOrder("FLAGWINS", PaymentMethod.Sepay, OrderStatus.Cancelled, PaymentStatus.Unpaid, Vn(10, 9), 0,
            ["payment_expired"], (ProductP1, 1, 70_000));
        AddHistory(order, OrderStatus.PendingPayment, OrderStatus.Cancelled, "Khách tự hủy.", Vn(10, 10));
        await SaveSeedAsync();

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 10)), CancellationToken.None));

        Assert.Equal(new CancellationCounts(ByCustomer: 0, ByShop: 0, PaymentExpired: 1), Assert.Single(days).Cancellations);
    }

    [Fact]
    // Đơn hết hạn rồi được khôi phục khi khách trả muộn, sau đó khách tự hủy. Báo cáo lấy dòng hủy mới nhất, nên đơn là khách hủy.
    public async Task GetDailySales_OrderCancelledTwice_UsesLatestCancellationReason()
    {
        var order = AddOrder("TWICE", PaymentMethod.Sepay, OrderStatus.Cancelled, PaymentStatus.RefundPending, Vn(10, 9), 0,
            ["late_payment"], (ProductP1, 1, 70_000));
        AddHistory(order, OrderStatus.PendingPayment, OrderStatus.Cancelled, ExpiredReason, Vn(10, 10));
        AddHistory(order, OrderStatus.Cancelled, OrderStatus.PendingConfirmation, "SePay nhận đủ tiền sau khi hết hạn.", Vn(10, 11));
        AddHistory(order, OrderStatus.PendingConfirmation, OrderStatus.Cancelled, "Khách tự hủy.", Vn(10, 12));
        await SaveSeedAsync();

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 10)), CancellationToken.None));

        Assert.Equal(new CancellationCounts(ByCustomer: 1, ByShop: 0, PaymentExpired: 0), Assert.Single(days).Cancellations);
    }

    [Fact]
    // Hai dòng hủy trùng thời điểm thì dòng có mã lớn hơn thắng, để kết quả luôn xác định giữa các lần chạy.
    public async Task GetDailySales_CancellationRowsWithSameTimestamp_UseHighestIdAsTieBreaker()
    {
        var order = AddOrder("TIE", PaymentMethod.Cod, OrderStatus.Cancelled, PaymentStatus.Unpaid, Vn(10, 9), 0, [],
            (ProductP1, 1, 70_000));
        AddHistory(order, OrderStatus.PendingConfirmation, OrderStatus.Cancelled, "Khách tự hủy.", Vn(10, 10), id: "tie-z");
        AddHistory(order, OrderStatus.PendingConfirmation, OrderStatus.Cancelled, "Hết hàng.", Vn(10, 10), id: "tie-a");
        await SaveSeedAsync();

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 10)), CancellationToken.None));

        Assert.Equal(new CancellationCounts(ByCustomer: 1, ByShop: 0, PaymentExpired: 0), Assert.Single(days).Cancellations);
    }

    private List<CapturingLoggerProvider.CapturedLog> WarningsAboutMissingPaymentTime() =>
        _factory.Logs.Entries
            .Where(entry => entry.Level == LogLevel.Warning && entry.Category.EndsWith("CommerceReports", StringComparison.Ordinal))
            .ToList();

    [Theory]
    [InlineData(2040, 3, 12, 2040, 3, 10)]
    [InlineData(2040, 3, 11, 2040, 3, 10)]
    // Khoảng ngược không có nghĩa. Hàm ném `ArgumentException` với `paramName` là `range`.
    public async Task GetDailySales_FromAfterTo_ThrowsArgumentException(int fromYear, int fromMonth, int fromDay, int toYear, int toMonth, int toDay)
    {
        var range = new DateRange(new DateOnly(fromYear, fromMonth, fromDay), new DateOnly(toYear, toMonth, toDay));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => WithReportsAsync(reports => reports.GetDailySalesAsync(range, CancellationToken.None)));

        Assert.Equal("range", exception.ParamName);
    }

    [Fact]
    // Khoảng 366 ngày là giới hạn cho phép và trả đúng 366 dòng. Khoảng 367 ngày bị từ chối.
    public async Task GetDailySales_RangeLimit_Allows366DaysAndRejects367()
    {
        var from = new DateOnly(2042, 1, 1);

        var allowed = await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(from, from.AddDays(365)), CancellationToken.None));
        Assert.Equal(366, allowed.Count);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(from, from.AddDays(366)), CancellationToken.None)));
        Assert.Equal("range", exception.ParamName);
    }

    // ---------- GetProductSalesAsync ----------

    [Fact]
    // Kết quả gộp đúng theo sản phẩm, và bỏ đơn hoàn tiền, đơn ngoài khoảng, đơn chưa trả.
    // Thứ tự: doanh thu giảm dần, rồi số lượng giảm dần, rồi mã sản phẩm theo ordinal ('Z' đứng trước 'a').
    public async Task GetProductSales_Fixture_MatchesHandCalculatedTableAndOrder()
    {
        await SeedFixtureAsync();

        var products = await WithReportsAsync(reports => reports.GetProductSalesAsync(FixtureRange, CancellationToken.None));

        Assert.Equal(
            [
                new ProductSales(ProductP1, 1_800_000, 4),
                new ProductSales(ProductP2, 500_000, 5),
                new ProductSales(ProductP4, 500_000, 2),
                new ProductSales(ProductUpperZ, 200_000, 1),
                new ProductSales(ProductLowerA, 200_000, 1),
            ],
            products);
    }

    [Fact]
    // Doanh thu sản phẩm không gồm phí ship: một đơn 70.000 hàng cộng 30.000 phí ship chỉ góp 70.000.
    public async Task GetProductSales_ExcludesShippingFee()
    {
        var order = AddOrder("SHIPFEE", PaymentMethod.Sepay, OrderStatus.Confirmed, PaymentStatus.Paid, Vn(10, 9), 0, [],
            (ProductP1, 1, 70_000));
        AddTransaction(order, "matched", order.Total, Vn(10, 10));
        await SaveSeedAsync();

        var products = await WithReportsAsync(reports => reports.GetProductSalesAsync(
            new DateRange(new DateOnly(2040, 3, 10), new DateOnly(2040, 3, 10)), CancellationToken.None));

        Assert.Equal([new ProductSales(ProductP1, 70_000, 1)], products);
    }

    [Fact]
    // Khoảng không có đơn nào được ghi nhận trả danh sách rỗng.
    public async Task GetProductSales_RangeWithoutData_ReturnsEmptyList()
    {
        var products = await WithReportsAsync(reports => reports.GetProductSalesAsync(
            new DateRange(new DateOnly(2043, 1, 1), new DateOnly(2043, 1, 3)), CancellationToken.None));

        Assert.Empty(products);
    }

    [Fact]
    // Hàm bán theo sản phẩm dùng cùng luật khoảng ngày với hàm bán theo ngày.
    public async Task GetProductSales_InvalidRange_ThrowsArgumentException()
    {
        var reversed = new DateRange(new DateOnly(2040, 3, 12), new DateOnly(2040, 3, 10));
        var tooLong = new DateRange(new DateOnly(2042, 1, 1), new DateOnly(2042, 1, 1).AddDays(366));

        var reversedError = await Assert.ThrowsAsync<ArgumentException>(
            () => WithReportsAsync(reports => reports.GetProductSalesAsync(reversed, CancellationToken.None)));
        var tooLongError = await Assert.ThrowsAsync<ArgumentException>(
            () => WithReportsAsync(reports => reports.GetProductSalesAsync(tooLong, CancellationToken.None)));

        Assert.Equal("range", reversedError.ParamName);
        Assert.Equal("range", tooLongError.ParamName);
    }

    // ---------- GetPendingCountsAsync ----------

    [Fact]
    // Ba số khớp dữ liệu: bốn đơn chờ xác nhận, hai giao dịch chưa khớp, một đơn COD đã giao chưa nhận tiền.
    // Giao dịch `matched` và `ignored`, đơn Sepay đã giao chưa trả và đơn COD đã nhận tiền không được đếm.
    public async Task GetPendingCounts_Fixture_MatchesData()
    {
        await SeedFixtureAsync();

        var counts = await WithReportsAsync(reports => reports.GetPendingCountsAsync(CancellationToken.None));

        Assert.Equal(new PendingCounts(PendingConfirmation: 4, UnmatchedPayments: 2, CodUnreceived: 1), counts);
    }

    [Fact]
    // Không có dữ liệu thì cả ba số bằng 0.
    public async Task GetPendingCounts_WithoutData_ReturnsZeros()
    {
        var counts = await WithReportsAsync(reports => reports.GetPendingCountsAsync(CancellationToken.None));

        Assert.Equal(new PendingCounts(0, 0, 0), counts);
    }

    // ---------- Luồng thật ----------

    [Fact]
    // Route thật gồm khách tự hủy, nhân viên hủy, nhân viên nhận COD và job hết hạn thanh toán.
    // Kết quả báo cáo phân loại đúng ba lý do hủy, nên code ghi lịch sử và code báo cáo dùng cùng hằng.
    public async Task RealFlows_CustomerCancel_StaffCancel_CodReceipt_AndExpiry_AreClassifiedByReport()
    {
        await using var kit = new PaymentExpiryTestKit(_factory, _database);
        await kit.InitializeAsync();
        await IdentityTestData.SeedRolesAsync(_database);
        var (_, session) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "com23");
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var customerCancelled = await kit.CreateOrderAsync("cod");
        var staffCancelled = await kit.CreateOrderAsync("cod");
        var codDelivered = await kit.CreateOrderAsync("cod");
        var expired = await kit.CreateOrderAsync("sepay");
        _auditedCodes.AddRange([staffCancelled.Code, codDelivered.Code]);

        // Khách tự hủy qua route của khách, bằng token trả về lúc đặt đơn.
        await kit.CancelAsync(customerCancelled);

        // Nhân viên hủy qua route của nhân viên, với lý do tự do.
        var cancelResponse = await client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, $"/api/admin/orders/{staffCancelled.Code}/cancellation", new { reason = "Hết hàng." }, $"pk_session={session}"));
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        // Đơn COD phải ở trạng thái đã giao trước khi nhân viên đánh dấu nhận tiền.
        await using (var commerce = _database.CreateCommerceContext())
        {
            var order = await commerce.Orders.SingleAsync(row => row.Id == codDelivered.Id);
            order.Status = OrderStatus.Delivered;
            await commerce.SaveChangesAsync();
        }

        var receiptResponse = await client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Put, $"/api/admin/orders/{codDelivered.Code}/cod-receipt", null, $"pk_session={session}"));
        Assert.Equal(HttpStatusCode.OK, receiptResponse.StatusCode);

        // Job hết hạn hủy đơn Sepay quá hạn, dùng đồng hồ giả của host test.
        using (var scope = kit.CreateHost().Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ExpiredPaymentProcessor>().ProcessAsync(CancellationToken.None);
        }

        // Dời mọi mốc thời gian sang một ngày cố định. Báo cáo không lẫn dữ liệu của test khác và không phụ thuộc giờ máy.
        var flowDayStartUtc = new DateTime(2042, 6, 1, 3, 0, 0, DateTimeKind.Utc);
        await MoveOrdersToAsync([customerCancelled.Id, staffCancelled.Id, codDelivered.Id, expired.Id], flowDayStartUtc);

        var days = await WithReportsAsync(reports => reports.GetDailySalesAsync(
            new DateRange(new DateOnly(2042, 6, 1), new DateOnly(2042, 6, 1)), CancellationToken.None));

        var day = Assert.Single(days);
        Assert.Equal(4, day.CreatedOrderCount);
        Assert.Equal(new CancellationCounts(ByCustomer: 1, ByShop: 1, PaymentExpired: 1), day.Cancellations);
        // Đơn hết hạn không vào giá trị đơn đặt. Hai đơn COD đã hủy cũng không vào bộ đếm phương thức.
        Assert.Equal(customerCancelled.Total + staffCancelled.Total + codDelivered.Total, day.PlacedOrderValue);
        Assert.Equal(new PaymentMethodCounts(Cod: 1, Sepay: 0), day.PaymentMethods);
        // Chỉ đơn COD đã nhận tiền có doanh thu, xếp theo ngày nhận tiền.
        Assert.Equal(codDelivered.Total, day.RecognizedRevenue);
        Assert.Equal(1, day.PaidOrderCount);
    }

    // Dời ngày tạo đơn và các dòng lịch sử về quanh `startUtc`. Thứ tự các dòng lịch sử được giữ nguyên.
    private async Task MoveOrdersToAsync(IReadOnlyList<string> orderIds, DateTime startUtc)
    {
        await using var commerce = _database.CreateCommerceContext();
        foreach (var orderId in orderIds)
        {
            var order = await commerce.Orders.Include(row => row.StatusHistory).SingleAsync(row => row.Id == orderId);
            order.CreatedAt = startUtc;
            var minutesAfterStart = 1;
            foreach (var history in order.StatusHistory.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id))
            {
                history.CreatedAt = startUtc.AddMinutes(minutesAfterStart);
                minutesAfterStart++;
            }
        }

        await commerce.SaveChangesAsync();
    }
}
