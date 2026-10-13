using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Payments;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-13: nhân viên gắn tay giao dịch SePay vào đơn hoặc đánh dấu không thuộc đơn nào.
// Luật khớp dùng chung với webhook, nên test đối chiếu kết quả với hành vi của webhook.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminPaymentReconciliationTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string TransactionsPath = "/api/admin/payments/transactions";
    private const string OrdersPath = "/api/orders";
    private const string WebOrigin = "http://localhost:5173";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";
    private const string OrderAuditType = "order";
    private const string TransactionAuditType = "payment_transaction";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];
    private readonly List<string> _transactionIds = [];

    public AdminPaymentReconciliationTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new CartTestKit(factory, database);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await _kit.InitializeAsync();
        await QuoteTestSetup.ResetAsync(_factory);
        await QuoteTestSetup.SeedCatalogAsync(_factory);
        await QuoteTestSetup.SeedSettingsAsync(_factory, defaultServiceTypeId: 2);
        _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public async Task DisposeAsync()
    {
        await using (var identity = _database.CreateIdentityContext())
        {
            await identity.AuditLogs
                .Where(log => log.TargetType == TransactionAuditType && _transactionIds.Contains(log.TargetId))
                .ExecuteDeleteAsync();
        }

        await using (var commerce = _database.CreateCommerceContext())
        {
            var orderIds = await commerce.Orders.Where(order => _keys.Contains(order.IdempotencyKey))
                .Select(order => order.Id).ToListAsync();
            // Xóa giao dịch trước đơn vì khóa ngoại cố ý chặn xóa đơn còn giao dịch gắn vào.
            await commerce.PaymentTransactions.Where(row => _transactionIds.Contains(row.Id)).ExecuteDeleteAsync();
            await commerce.OrderStatusHistory.Where(history => orderIds.Contains(history.OrderId)).ExecuteDeleteAsync();
            await commerce.OrderItems.Where(item => orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await commerce.Orders.Where(order => orderIds.Contains(order.Id)).ExecuteDeleteAsync();
        }

        await _kit.CleanupAsync();
    }

    private sealed record PlacedOrder(string Id, string Code, string SkuId, int Total);

    // Đặt đơn bằng route thật của khách để có kho đã giữ và bản chụp đúng như môi trường chạy.
    private async Task<PlacedOrder> PlaceOrderAsync(string paymentMethod = "sepay")
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = await _kit.AddProductAsync(price: 100_000, stock: 10);
        var skuId = product.SkuIds[0];
        Assert.Equal(HttpStatusCode.OK, (await _kit.AddAsync(anonymousId, skuId, 2)).StatusCode);

        var address = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" };
        var preview = await _kit.SendAsync(HttpMethod.Post, "/api/checkout/previews", anonymousId, new { address, paymentMethod });
        var total = (await CartTestKit.ReadCartAsync(preview)).GetProperty("total").GetInt32();

        var orderBody = new
        {
            contact = new { name = "Nguyễn Văn An", phone = "0901234567", email = "an@example.com" },
            address,
            paymentMethod,
            expectedTotal = total,
        };
        var key = "com13-" + Guid.NewGuid().ToString("N");
        _keys.Add(key);
        var request = new HttpRequestMessage(HttpMethod.Post, OrdersPath);
        request.Headers.Add("Cookie", $"pk_anon={anonymousId}");
        request.Headers.Add("Origin", WebOrigin);
        request.Headers.Add("Idempotency-Key", key);
        request.Content = new StringContent(
            JsonSerializer.Serialize(orderBody), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var commerce = _database.CreateCommerceContext();
        var order = await commerce.Orders.AsNoTracking().SingleAsync(row => row.IdempotencyKey == key);
        return new PlacedOrder(order.Id, order.Code, skuId, order.Total);
    }

    // Giao dịch chưa khớp mặc định, như webhook để lại khi nội dung chuyển khoản không có mã đơn.
    private async Task<string> AddTransactionAsync(int amount, string matchStatus = "unmatched", string? orderId = null)
    {
        var id = Guid.NewGuid().ToString();
        _transactionIds.Add(id);
        await using var commerce = _database.CreateCommerceContext();
        commerce.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = id,
            Provider = "sepay",
            ProviderTxId = "com13-" + id,
            Amount = amount,
            Content = "chuyen khoan khong ro ma",
            Raw = JsonSerializer.SerializeToDocument(new { id }),
            OrderId = orderId,
            MatchStatus = matchStatus,
            ReceivedAt = DateTime.UtcNow,
        });
        await commerce.SaveChangesAsync();
        return id;
    }

    private async Task SetOrderStateAsync(
        string code, OrderStatus? status = null, PaymentStatus? paymentStatus = null, DateTime? paymentDeadline = null)
    {
        await using var commerce = _database.CreateCommerceContext();
        var order = await commerce.Orders.SingleAsync(row => row.Code == code);
        order.Status = status ?? order.Status;
        order.PaymentStatus = paymentStatus ?? order.PaymentStatus;
        order.PaymentDeadline = paymentDeadline ?? order.PaymentDeadline;
        await commerce.SaveChangesAsync();
    }

    private async Task<(string UserId, string Session)> StaffAsync(string roleCode)
    {
        var (user, session) = await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com13");
        return (user.Id, session);
    }

    private Task<HttpResponseMessage> PatchAsync(string transactionId, string? session, object? body) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Patch, $"{TransactionsPath}/{transactionId}", body, session is null ? null : $"pk_session={session}"));

    private Task<HttpResponseMessage> AttachAsync(string transactionId, string? session, string orderCode) =>
        PatchAsync(transactionId, session, new { orderCode });

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(code, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<PaymentTransaction> TransactionAsync(string id)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.PaymentTransactions.AsNoTracking().SingleAsync(row => row.Id == id);
    }

    private async Task<Order> OrderAsync(string code)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().Include(row => row.StatusHistory).SingleAsync(row => row.Code == code);
    }

    private async Task<List<Pickory.Modules.Identity.Entities.AuditLog>> AuditOfAsync(string transactionId, string action)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.AsNoTracking()
            .Where(log => log.TargetType == TransactionAuditType && log.TargetId == transactionId && log.Action == action)
            .ToListAsync();
    }

    private int PaidEventCount(string code) =>
        _factory.DomainEvents.OfType<OrderPaidEvent>().Count(e => e.Code == code);

    private async Task AssertNothingChangedAsync(string transactionId, string code, int historyBefore)
    {
        var transaction = await TransactionAsync(transactionId);
        Assert.Equal("unmatched", transaction.MatchStatus);
        Assert.Null(transaction.OrderId);
        Assert.Equal(historyBefore, (await OrderAsync(code)).StatusHistory.Count);
        Assert.Empty(await AuditOfAsync(transactionId, "commerce.payment.attach"));
        Assert.Equal(0, PaidEventCount(code));
    }

    // ---------- Gắn vào đơn ----------

    [Fact]
    // Gắn giao dịch đủ tiền vào đơn `pending_payment` cho kết quả như webhook tự khớp.
    // Test kiểm một sự kiện, một audit và một dòng lịch sử mang mã nhân viên.
    public async Task Attach_FullAmountToPendingPaymentOrder_ConfirmsLikeWebhook()
    {
        var order = await PlaceOrderAsync();
        var transactionId = await AddTransactionAsync(order.Total);
        var (staffId, session) = await StaffAsync("sales");

        var data = await ReadDataAsync(await AttachAsync(transactionId, session, order.Code));

        Assert.Equal("matched", data.GetProperty("matchStatus").GetString());
        Assert.Equal(order.Code, data.GetProperty("order").GetProperty("code").GetString());
        var saved = await OrderAsync(order.Code);
        Assert.Equal(OrderStatus.PendingConfirmation, saved.Status);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.DoesNotContain("underpaid", saved.Flags);
        Assert.DoesNotContain("overpaid", saved.Flags);

        var transaction = await TransactionAsync(transactionId);
        Assert.Equal("matched", transaction.MatchStatus);
        Assert.Equal(order.Id, transaction.OrderId);

        var history = Assert.Single(saved.StatusHistory, row => row.ToStatus == OrderStatus.PendingConfirmation);
        Assert.Equal(OrderStatus.PendingPayment, history.FromStatus);
        Assert.Equal(staffId, history.ActorId);
        Assert.Equal("Nhân viên gắn giao dịch SePay vào đơn.", history.Reason);

        var paidEvent = Assert.Single(_factory.DomainEvents.OfType<OrderPaidEvent>(), e => e.Code == order.Code);
        Assert.Equal(order.Total, paidEvent.Amount);

        var audit = Assert.Single(await AuditOfAsync(transactionId, "commerce.payment.attach"));
        Assert.Equal(staffId, audit.ActorId);
        Assert.Equal("unmatched", audit.Before!.RootElement.GetProperty("matchStatus").GetString());
        Assert.Equal(JsonValueKind.Null, audit.Before.RootElement.GetProperty("orderCode").ValueKind);
        Assert.Equal("matched", audit.After!.RootElement.GetProperty("matchStatus").GetString());
        Assert.Equal(order.Code, audit.After.RootElement.GetProperty("orderCode").GetString());
    }

    [Fact]
    // Gắn giao dịch thiếu tiền thì giao dịch `matched` và đơn có cờ `underpaid`. Đơn chưa trả và không có sự kiện.
    public async Task Attach_UnderpaidAmount_FlagsOrderWithoutPaying()
    {
        var order = await PlaceOrderAsync();
        var transactionId = await AddTransactionAsync(order.Total - 1);
        var (_, session) = await StaffAsync("sales");

        await ReadDataAsync(await AttachAsync(transactionId, session, order.Code));

        var saved = await OrderAsync(order.Code);
        Assert.Equal(OrderStatus.PendingPayment, saved.Status);
        Assert.Equal(PaymentStatus.Unpaid, saved.PaymentStatus);
        Assert.Contains("underpaid", saved.Flags);
        Assert.Equal("matched", (await TransactionAsync(transactionId)).MatchStatus);
        Assert.Equal(0, PaidEventCount(order.Code));
        Assert.Single(await AuditOfAsync(transactionId, "commerce.payment.attach"));
    }

    [Fact]
    // Gắn giao dịch thừa tiền: đơn được ghi nhận đã trả và mang cờ `overpaid`.
    public async Task Attach_OverpaidAmount_PaysOrderAndFlagsOverpaid()
    {
        var order = await PlaceOrderAsync();
        var transactionId = await AddTransactionAsync(order.Total + 5_000);
        var (_, session) = await StaffAsync("sales");

        await ReadDataAsync(await AttachAsync(transactionId, session, order.Code));

        var saved = await OrderAsync(order.Code);
        Assert.Equal(OrderStatus.PendingConfirmation, saved.Status);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Contains("overpaid", saved.Flags);
        Assert.Equal(order.Total + 5_000, Assert.Single(_factory.DomainEvents.OfType<OrderPaidEvent>(), e => e.Code == order.Code).Amount);
    }

    [Fact]
    // Đơn hủy vì hết hạn đi qua luật tiền về muộn của webhook: giữ lại hàng, khôi phục đơn, gắn cờ `late_payment`.
    public async Task Attach_ToExpiredOrder_RestoresLikeLatePayment()
    {
        var order = await PlaceOrderAsync();
        await SetOrderStateAsync(order.Code, paymentDeadline: DateTime.UtcNow.AddMinutes(-1));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ExpiredPaymentProcessor>().ProcessAsync(CancellationToken.None);
        }

        var expired = await OrderAsync(order.Code);
        Assert.Equal(OrderStatus.Cancelled, expired.Status);
        Assert.Contains("payment_expired", expired.Flags);
        Assert.Equal(10, await _kit.StockOfAsync(order.SkuId));

        var transactionId = await AddTransactionAsync(order.Total);
        var (staffId, session) = await StaffAsync("sales");

        await ReadDataAsync(await AttachAsync(transactionId, session, order.Code));

        var saved = await OrderAsync(order.Code);
        Assert.Equal(OrderStatus.PendingConfirmation, saved.Status);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Equal(8, await _kit.StockOfAsync(order.SkuId));
        Assert.DoesNotContain("payment_expired", saved.Flags);
        Assert.Contains("late_payment", saved.Flags);
        var restored = Assert.Single(saved.StatusHistory, row =>
            row.FromStatus == OrderStatus.Cancelled && row.ToStatus == OrderStatus.PendingConfirmation);
        Assert.Equal(staffId, restored.ActorId);
        Assert.Equal("matched", (await TransactionAsync(transactionId)).MatchStatus);
        Assert.Single(_factory.DomainEvents.OfType<OrderPaidEvent>(), e => e.Code == order.Code);
        Assert.Single(await AuditOfAsync(transactionId, "commerce.payment.attach"));
    }

    [Theory]
    [InlineData("cod", OrderStatus.PendingConfirmation, PaymentStatus.Unpaid)]
    [InlineData("sepay", OrderStatus.PendingConfirmation, PaymentStatus.Paid)]
    [InlineData("sepay", OrderStatus.Cancelled, PaymentStatus.Unpaid)]
    // Đơn COD, đơn đã trả và đơn hủy vì lý do khác (không có cờ hết hạn) không nhận được tiền SePay.
    // Webhook để chứng từ chưa khớp, còn route gắn tay trả 409 và không đổi gì.
    public async Task Attach_ToOrderThatCannotReceivePayment_Returns409AndChangesNothing(
        string paymentMethod, OrderStatus status, PaymentStatus paymentStatus)
    {
        var order = await PlaceOrderAsync(paymentMethod);
        await SetOrderStateAsync(order.Code, status, paymentStatus);
        var transactionId = await AddTransactionAsync(order.Total);
        var (_, session) = await StaffAsync("sales");
        var before = await OrderAsync(order.Code);

        var response = await AttachAsync(transactionId, session, order.Code);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_PAYMENT_MATCH");
        await AssertNothingChangedAsync(transactionId, order.Code, before.StatusHistory.Count);
        var after = await OrderAsync(order.Code);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.PaymentStatus, after.PaymentStatus);
        Assert.Equal(before.Flags, after.Flags);
    }

    [Fact]
    public async Task Attach_UnknownOrder_Returns404()
    {
        var transactionId = await AddTransactionAsync(100_000);
        var (_, session) = await StaffAsync("sales");

        var response = await AttachAsync(transactionId, session, "PKZZZZZZZZ");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "ORDER_NOT_FOUND");
        Assert.Equal("unmatched", (await TransactionAsync(transactionId)).MatchStatus);
        Assert.Empty(await AuditOfAsync(transactionId, "commerce.payment.attach"));
    }

    [Fact]
    public async Task Attach_UnknownTransaction_Returns404()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");

        var response = await AttachAsync(Guid.NewGuid().ToString(), session, order.Code);

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "PAYMENT_TRANSACTION_NOT_FOUND");
        Assert.Equal(PaymentStatus.Unpaid, (await OrderAsync(order.Code)).PaymentStatus);
    }

    [Theory]
    [InlineData("matched")]
    [InlineData("ignored")]
    // Chỉ giao dịch `unmatched` mới gắn hoặc bỏ qua được. Giao dịch đã `matched` hoặc `ignored` trả 409 và không đổi.
    public async Task Update_AlreadyResolvedTransaction_Returns409(string matchStatus)
    {
        var order = await PlaceOrderAsync();
        var transactionId = await AddTransactionAsync(order.Total, matchStatus);
        var (_, session) = await StaffAsync("sales");

        await AssertErrorAsync(
            await AttachAsync(transactionId, session, order.Code), HttpStatusCode.Conflict, "INVALID_PAYMENT_MATCH");
        await AssertErrorAsync(
            await PatchAsync(transactionId, session, new { matchStatus = "ignored" }),
            HttpStatusCode.Conflict,
            "INVALID_PAYMENT_MATCH");

        Assert.Equal(matchStatus, (await TransactionAsync(transactionId)).MatchStatus);
        Assert.Equal(PaymentStatus.Unpaid, (await OrderAsync(order.Code)).PaymentStatus);
        Assert.Empty(await AuditOfAsync(transactionId, "commerce.payment.attach"));
        Assert.Empty(await AuditOfAsync(transactionId, "commerce.payment.ignore"));
        Assert.Equal(0, PaidEventCount(order.Code));
    }

    [Fact]
    // Mã đơn so sánh không phân biệt hoa thường và bỏ khoảng trắng hai đầu, giống `TransferContentParser`.
    public async Task Attach_LowercaseOrderCodeWithSpaces_Attaches()
    {
        var order = await PlaceOrderAsync();
        var transactionId = await AddTransactionAsync(order.Total);
        var (_, session) = await StaffAsync("sales");

        await ReadDataAsync(await AttachAsync(transactionId, session, "  " + order.Code.ToLowerInvariant() + "  "));

        Assert.Equal(PaymentStatus.Paid, (await OrderAsync(order.Code)).PaymentStatus);
    }

    [Fact]
    // Test giữ khóa dòng giao dịch rồi gửi hai request, nên có khóa dòng thì chưa request nào xong.
    // Sau khi nhả khóa, một request đạt và request kia nhận 409, với một sự kiện và một audit.
    public async Task Attach_ConcurrentRequests_AreSerializedByTransactionRowLock()
    {
        var order = await PlaceOrderAsync();
        var transactionId = await AddTransactionAsync(order.Total);
        var (_, firstSession) = await StaffAsync("sales");
        var (_, secondSession) = await StaffAsync("sales");

        await using var holder = _database.CreateCommerceContext();
        await using var holderTransaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM payment_transactions WHERE "id" = {transactionId} FOR UPDATE""");

        var pending = Task.WhenAll(
            AttachAsync(transactionId, firstSession, order.Code),
            AttachAsync(transactionId, secondSession, order.Code));
        var finishedWhileLocked = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2))) == pending;

        await holderTransaction.CommitAsync();
        var responses = await pending;

        Assert.False(finishedWhileLocked, "Request gắn tay phải chờ khóa dòng giao dịch.");
        var statusCodes = responses.Select(response => response.StatusCode).OrderBy(code => (int)code).ToList();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statusCodes);
        Assert.Equal(1, PaidEventCount(order.Code));
        Assert.Single(await AuditOfAsync(transactionId, "commerce.payment.attach"));
        Assert.Single((await OrderAsync(order.Code)).StatusHistory, row => row.ToStatus == OrderStatus.PendingConfirmation);
    }

    [Fact]
    // Hai nhân viên gắn cùng giao dịch vào hai đơn khác nhau, nên khóa dòng đơn không xếp hàng được hai request.
    // Chỉ khóa dòng giao dịch chặn được: một đơn được trả, đơn kia không đổi.
    public async Task Attach_SameTransactionToTwoOrders_OnlyOneOrderIsPaid()
    {
        var firstOrder = await PlaceOrderAsync();
        var secondOrder = await PlaceOrderAsync();
        var transactionId = await AddTransactionAsync(firstOrder.Total);
        var (_, firstSession) = await StaffAsync("sales");
        var (_, secondSession) = await StaffAsync("sales");

        await using var holder = _database.CreateCommerceContext();
        await using var holderTransaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM payment_transactions WHERE "id" = {transactionId} FOR UPDATE""");

        var pending = Task.WhenAll(
            AttachAsync(transactionId, firstSession, firstOrder.Code),
            AttachAsync(transactionId, secondSession, secondOrder.Code));
        var finishedWhileLocked = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2))) == pending;

        await holderTransaction.CommitAsync();
        var responses = await pending;

        Assert.False(finishedWhileLocked, "Request gắn tay phải chờ khóa dòng giao dịch.");
        var statusCodes = responses.Select(response => response.StatusCode).OrderBy(code => (int)code).ToList();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statusCodes);
        var paidOrders = new[] { firstOrder, secondOrder }.Count(order => PaidEventCount(order.Code) == 1);
        Assert.Equal(1, paidOrders);
        Assert.Single(await AuditOfAsync(transactionId, "commerce.payment.attach"));
        var transaction = await TransactionAsync(transactionId);
        Assert.Equal("matched", transaction.MatchStatus);
        var statuses = new[] { await OrderAsync(firstOrder.Code), await OrderAsync(secondOrder.Code) };
        Assert.Single(statuses, order => order.PaymentStatus == PaymentStatus.Paid);
        Assert.Equal(statuses.Single(order => order.PaymentStatus == PaymentStatus.Paid).Id, transaction.OrderId);
    }

    // ---------- Đánh dấu bỏ qua ----------

    [Fact]
    // Đánh dấu `ignored` chỉ đổi giao dịch, ghi một audit và không chạm vào đơn nào.
    public async Task Ignore_UnmatchedTransaction_MarksIgnoredWithOneAudit()
    {
        var transactionId = await AddTransactionAsync(100_000);
        var (staffId, session) = await StaffAsync("sales");

        var data = await ReadDataAsync(await PatchAsync(transactionId, session, new { matchStatus = "ignored" }));

        Assert.Equal("ignored", data.GetProperty("matchStatus").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("order").ValueKind);
        var transaction = await TransactionAsync(transactionId);
        Assert.Equal("ignored", transaction.MatchStatus);
        Assert.Null(transaction.OrderId);

        var audit = Assert.Single(await AuditOfAsync(transactionId, "commerce.payment.ignore"));
        Assert.Equal(staffId, audit.ActorId);
        Assert.Equal("unmatched", audit.Before!.RootElement.GetProperty("matchStatus").GetString());
        Assert.Equal("ignored", audit.After!.RootElement.GetProperty("matchStatus").GetString());
        Assert.Empty(await AuditOfAsync(transactionId, "commerce.payment.attach"));
    }

    [Fact]
    // Body sai bị từ chối trước khi khóa dòng. Giao dịch giữ nguyên và không có audit.
    public async Task Update_InvalidBody_Returns400AndChangesNothing()
    {
        var order = await PlaceOrderAsync();
        var transactionId = await AddTransactionAsync(order.Total);
        var (_, session) = await StaffAsync("sales");

        foreach (var body in new object?[]
                 {
                     null,
                     new { },
                     new { orderCode = order.Code, matchStatus = "ignored" },
                     new { matchStatus = "matched" },
                     new { matchStatus = "Ignored" },
                     new { matchStatus = 1 },
                     new { orderCode = "" },
                     new { orderCode = "   " },
                     new { orderCode = 123 },
                     new { orderCode = order.Code, extra = "x" },
                 })
        {
            await AssertErrorAsync(
                await PatchAsync(transactionId, session, body), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }

        Assert.Equal("unmatched", (await TransactionAsync(transactionId)).MatchStatus);
        Assert.Equal(PaymentStatus.Unpaid, (await OrderAsync(order.Code)).PaymentStatus);
        Assert.Empty(await AuditOfAsync(transactionId, "commerce.payment.attach"));
        Assert.Empty(await AuditOfAsync(transactionId, "commerce.payment.ignore"));
    }

    [Fact]
    // `support` không có `payments:reconcile` nên nhận 403, khách vãng lai nhận 401. Giao dịch không đổi.
    public async Task Update_WithoutPermission_IsRejectedAndTransactionUnchanged()
    {
        var order = await PlaceOrderAsync();
        var transactionId = await AddTransactionAsync(order.Total);
        var (_, supportSession) = await StaffAsync("support");

        await AssertErrorAsync(
            await AttachAsync(transactionId, supportSession, order.Code), HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(HttpStatusCode.Unauthorized, (await AttachAsync(transactionId, null, order.Code)).StatusCode);
        Assert.Equal("unmatched", (await TransactionAsync(transactionId)).MatchStatus);
        Assert.Empty(await AuditOfAsync(transactionId, "commerce.payment.attach"));
    }
}
