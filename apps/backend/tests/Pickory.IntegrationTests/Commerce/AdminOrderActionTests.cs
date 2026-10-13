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
using Pickory.Modules.Shipping.Contracts;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-15: nhân viên xác nhận, hủy và đánh dấu đã xuất hóa đơn. Mỗi thao tác ghi lịch sử, audit và sự kiện.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminOrderActionTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string AdminOrdersPath = "/api/admin/orders";
    private const string OrdersPath = "/api/orders";
    private const string WebOrigin = "http://localhost:5173";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";
    private const string AuditTargetType = "order";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];
    private readonly List<string> _codes = [];

    public AdminOrderActionTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
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
        _factory.StockReleaseGate.Disarm();

        await using (var identity = _database.CreateIdentityContext())
        {
            await identity.AuditLogs
                .Where(log => log.TargetType == AuditTargetType && _codes.Contains(log.TargetId))
                .ExecuteDeleteAsync();
        }

        await using (var shipping = _database.CreateShippingContext())
        {
            var shipmentIds = await shipping.Shipments.Where(shipment => _codes.Contains(shipment.Ref))
                .Select(shipment => shipment.Id).ToListAsync();
            await shipping.ShipmentEvents.Where(shipmentEvent => shipmentIds.Contains(shipmentEvent.ShipmentId)).ExecuteDeleteAsync();
            await shipping.Shipments.Where(shipment => shipmentIds.Contains(shipment.Id)).ExecuteDeleteAsync();
        }

        await using (var commerce = _database.CreateCommerceContext())
        {
            var orderIds = await commerce.Orders.Where(order => _keys.Contains(order.IdempotencyKey))
                .Select(order => order.Id).ToListAsync();
            await commerce.OrderStatusHistory.Where(history => orderIds.Contains(history.OrderId)).ExecuteDeleteAsync();
            await commerce.OrderItems.Where(item => orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await commerce.Orders.Where(order => orderIds.Contains(order.Id)).ExecuteDeleteAsync();
        }

        await _kit.CleanupAsync();
    }

    private sealed record PlacedOrder(string Code, string SkuId);

    // Đặt đơn bằng route thật của khách để có kho đã giữ và bản chụp đúng như môi trường chạy.
    private async Task<PlacedOrder> PlaceOrderAsync(
        string paymentMethod = "cod", int quantity = 2, int stock = 10, bool withInvoice = false)
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = await _kit.AddProductAsync(price: 100_000, stock: stock);
        var skuId = product.SkuIds[0];
        var addResponse = await _kit.AddAsync(anonymousId, skuId, quantity);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var previewBody = new
        {
            address = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            paymentMethod,
        };
        var previewResponse = await _kit.SendAsync(HttpMethod.Post, "/api/checkout/previews", anonymousId, previewBody);
        var previewData = await CartTestKit.ReadCartAsync(previewResponse);
        var total = previewData.GetProperty("total").GetInt32();

        var orderBody = new Dictionary<string, object?>
        {
            ["contact"] = new { name = "Nguyễn Văn An", phone = "0901234567", email = "an@example.com" },
            ["address"] = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            ["paymentMethod"] = paymentMethod,
            ["expectedTotal"] = total,
        };
        if (withInvoice)
        {
            orderBody["invoice"] = new
            {
                companyName = "Công ty Thử",
                taxCode = "0312345678",
                address = "1 Đường Thử",
                email = "ketoan@example.com",
            };
        }

        var key = "key-" + Guid.NewGuid().ToString("N");
        _keys.Add(key);
        var request = new HttpRequestMessage(HttpMethod.Post, OrdersPath);
        request.Headers.Add("Cookie", $"pk_anon={anonymousId}");
        request.Headers.Add("Origin", WebOrigin);
        request.Headers.Add("Idempotency-Key", key);
        request.Content = new StringContent(
            JsonSerializer.Serialize(orderBody), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await CartTestKit.ReadJsonAsync(response);
        var code = json.GetProperty("data").GetProperty("code").GetString()!;
        _codes.Add(code);
        return new PlacedOrder(code, skuId);
    }

    private async Task SetOrderStateAsync(string code, OrderStatus? status = null, PaymentStatus? paymentStatus = null)
    {
        await using var commerce = _database.CreateCommerceContext();
        var order = await commerce.Orders.SingleAsync(row => row.Code == code);
        if (status is { } newStatus)
        {
            order.Status = newStatus;
        }

        if (paymentStatus is { } newPaymentStatus)
        {
            order.PaymentStatus = newPaymentStatus;
        }

        await commerce.SaveChangesAsync();
    }

    private async Task<(string UserId, string Session)> StaffAsync(string roleCode)
    {
        var (user, session) = await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com15");
        return (user.Id, session);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? session, object? body = null) =>
        _client.SendAsync(ActorHttp.CreateJsonRequest(method, path, body, session is null ? null : $"pk_session={session}"));

    private Task<HttpResponseMessage> ConfirmAsync(string code, string? session) =>
        SendAsync(HttpMethod.Post, $"{AdminOrdersPath}/{code}/confirmation", session);

    private Task<HttpResponseMessage> CancelAsync(string code, string? session, object? body) =>
        SendAsync(HttpMethod.Post, $"{AdminOrdersPath}/{code}/cancellation", session, body);

    private Task<HttpResponseMessage> IssueInvoiceAsync(string code, string? session) =>
        SendAsync(HttpMethod.Put, $"{AdminOrdersPath}/{code}/invoice-issuance", session);

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

    private async Task<List<(OrderStatus From, OrderStatus To, string? ActorId, string? Reason)>> HistoryOfAsync(string code)
    {
        await using var commerce = _database.CreateCommerceContext();
        var rows = await commerce.OrderStatusHistory.AsNoTracking()
            .Where(history => history.Order!.Code == code)
            .OrderBy(history => history.CreatedAt).ThenBy(history => history.Id)
            .Select(history => new { history.FromStatus, history.ToStatus, history.ActorId, history.Reason })
            .ToListAsync();
        return rows.Select(row => (row.FromStatus, row.ToStatus, row.ActorId, row.Reason)).ToList();
    }

    private async Task<List<Pickory.Modules.Identity.Entities.AuditLog>> AuditOfAsync(string code, string action)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.AsNoTracking()
            .Where(log => log.TargetType == AuditTargetType && log.TargetId == code && log.Action == action)
            .ToListAsync();
    }

    private static string OrderLabelOf(OrderStatus status) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString());

    private int StatusEventCount(string code) =>
        _factory.DomainEvents.OfType<OrderStatusChangedEvent>().Count(e => e.Code == code);

    // ---------- Xác nhận ----------

    [Fact]
    // Xác nhận từ `pending_confirmation` thành công, ghi lịch sử kèm người xác nhận, một dòng audit và một sự kiện.
    public async Task Confirm_FromPendingConfirmation_SucceedsWithHistoryAuditAndEvent()
    {
        var order = await PlaceOrderAsync("cod");
        var (staffId, session) = await StaffAsync("sales");

        var response = await ConfirmAsync(order.Code, session);

        var data = await ReadDataAsync(response);
        Assert.Equal(order.Code, data.GetProperty("code").GetString());
        Assert.Equal("confirmed", data.GetProperty("status").GetString());

        var history = (await HistoryOfAsync(order.Code)).Single(row => row.To == OrderStatus.Confirmed);
        Assert.Equal(OrderStatus.PendingConfirmation, history.From);
        Assert.Equal(staffId, history.ActorId);

        var audit = Assert.Single(await AuditOfAsync(order.Code, "commerce.order.confirm"));
        Assert.Equal(staffId, audit.ActorId);

        var statusEvent = Assert.Single(
            _factory.DomainEvents.OfType<OrderStatusChangedEvent>(), e => e.Code == order.Code && e.Status == OrderStatus.Confirmed);
        Assert.Equal(OrderStatus.PendingConfirmation, statusEvent.PreviousStatus);
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipping)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Returned)]
    [InlineData(OrderStatus.Cancelled)]
    // Chỉ `pending_confirmation` mới xác nhận được. Mọi trạng thái khác trả 409 và không ghi gì.
    public async Task Confirm_FromAnyOtherStatus_Returns409AndWritesNothing(OrderStatus status)
    {
        var order = await PlaceOrderAsync("cod");
        await SetOrderStateAsync(order.Code, status);
        var (_, session) = await StaffAsync("sales");
        var historyBefore = (await HistoryOfAsync(order.Code)).Count;
        var eventsBefore = StatusEventCount(order.Code);

        var response = await ConfirmAsync(order.Code, session);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        Assert.Equal(historyBefore, (await HistoryOfAsync(order.Code)).Count);
        Assert.Empty(await AuditOfAsync(order.Code, "commerce.order.confirm"));
        Assert.Equal(eventsBefore, StatusEventCount(order.Code));
    }

    [Fact]
    public async Task Confirm_UnknownCode_Returns404()
    {
        var (_, session) = await StaffAsync("sales");

        var response = await ConfirmAsync("PK00000000", session);

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "ORDER_NOT_FOUND");
    }

    [Fact]
    // `support` không có `orders:update-status`, khách đã đăng nhập bị 403 và khách vãng lai bị 401. Đơn không đổi.
    public async Task Confirm_WithoutPermission_IsRejectedAndOrderUnchanged()
    {
        var order = await PlaceOrderAsync("cod");
        var (_, supportSession) = await StaffAsync("support");

        await AssertErrorAsync(await ConfirmAsync(order.Code, supportSession), HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(HttpStatusCode.Unauthorized, (await ConfirmAsync(order.Code, null)).StatusCode);
        Assert.Empty(await AuditOfAsync(order.Code, "commerce.order.confirm"));
        Assert.DoesNotContain(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Confirmed);
    }

    // ---------- Hủy ----------

    [Fact]
    // Thiếu body, thiếu lý do, lý do rỗng hoặc chỉ khoảng trắng đều bị từ chối trước khi chạm đơn.
    public async Task Cancel_MissingOrBlankReason_Returns400AndChangesNothing()
    {
        var order = await PlaceOrderAsync("cod", quantity: 2, stock: 10);
        var (_, session) = await StaffAsync("sales");

        foreach (var body in new object?[]
                 {
                     null,
                     new { },
                     new { reason = "" },
                     new { reason = "   " },
                     new { reason = 123 },
                 })
        {
            await AssertErrorAsync(
                await CancelAsync(order.Code, session, body), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }

        Assert.Equal(8, await _kit.StockOfAsync(order.SkuId));
        Assert.DoesNotContain(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Cancelled);
        Assert.Empty(await AuditOfAsync(order.Code, "commerce.order.cancel"));
    }

    [Fact]
    // Lý do dài đúng 500 ký tự được nhận. 501 ký tự bị từ chối.
    public async Task Cancel_ReasonLength_BoundaryIs500()
    {
        var tooLong = await PlaceOrderAsync("cod");
        var exact = await PlaceOrderAsync("cod");
        var (_, session) = await StaffAsync("sales");

        await AssertErrorAsync(
            await CancelAsync(tooLong.Code, session, new { reason = new string('a', 501) }),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");
        var accepted = await CancelAsync(exact.Code, session, new { reason = new string('a', 500) });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Theory]
    [InlineData("sepay", OrderStatus.PendingPayment)]
    [InlineData("cod", OrderStatus.PendingConfirmation)]
    [InlineData("cod", OrderStatus.Confirmed)]
    // Nhân viên hủy từ ba trạng thái đầu. Kho trả đủ, lịch sử có lý do đã cắt khoảng trắng và người hủy.
    public async Task Cancel_FromCancellableStatus_ReleasesStockAndRecordsReason(string paymentMethod, OrderStatus status)
    {
        var order = await PlaceOrderAsync(paymentMethod, quantity: 2, stock: 10);
        await SetOrderStateAsync(order.Code, status);
        Assert.Equal(8, await _kit.StockOfAsync(order.SkuId));
        var (staffId, session) = await StaffAsync("sales");

        var response = await CancelAsync(order.Code, session, new { reason = "  Khách đổi ý  " });

        var data = await ReadDataAsync(response);
        Assert.Equal("cancelled", data.GetProperty("status").GetString());
        Assert.Equal(10, await _kit.StockOfAsync(order.SkuId));

        var history = (await HistoryOfAsync(order.Code)).Single(row => row.To == OrderStatus.Cancelled);
        Assert.Equal(status, history.From);
        Assert.Equal("Khách đổi ý", history.Reason);
        Assert.Equal(staffId, history.ActorId);

        var audit = Assert.Single(await AuditOfAsync(order.Code, "commerce.order.cancel"));
        Assert.Equal(staffId, audit.ActorId);
        // Audit lưu trạng thái trước và sau, trạng thái thanh toán trước và sau, và lý do.
        Assert.Equal("Khách đổi ý", audit.After!.RootElement.GetProperty("reason").GetString());
        Assert.Equal("cancelled", audit.After.RootElement.GetProperty("status").GetString());
        Assert.NotNull(audit.Before);
        Assert.Equal(OrderLabelOf(status), audit.Before.RootElement.GetProperty("status").GetString());

        var statusEvent = Assert.Single(
            _factory.DomainEvents.OfType<OrderStatusChangedEvent>(), e => e.Code == order.Code && e.Status == OrderStatus.Cancelled);
        Assert.Equal(status, statusEvent.PreviousStatus);
    }

    [Fact]
    // Đơn đã trả tiền bị hủy thì thanh toán chuyển sang chờ hoàn tiền.
    public async Task Cancel_PaidOrder_SetsPaymentStatusToRefundPending()
    {
        var order = await PlaceOrderAsync("sepay");
        await SetOrderStateAsync(order.Code, OrderStatus.Confirmed, PaymentStatus.Paid);
        var (_, session) = await StaffAsync("sales");

        var data = await ReadDataAsync(await CancelAsync(order.Code, session, new { reason = "Hết hàng thật" }));

        Assert.Equal("cancelled", data.GetProperty("status").GetString());
        Assert.Equal("refund_pending", data.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    // Đơn đang giao mà vận đơn còn hiệu lực thì không hủy được. Kho, lịch sử và audit giữ nguyên.
    public async Task Cancel_ShippingOrderWithActiveShipment_Returns409()
    {
        var order = await PlaceOrderAsync("cod", quantity: 2, stock: 10);
        await SetOrderStateAsync(order.Code, OrderStatus.Shipping);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var shipping = scope.ServiceProvider.GetRequiredService<IShippingService>();
            await shipping.CreateShipmentAsync(ShipmentTestSetup.CreateRequest(order.Code), CancellationToken.None);
        }

        var (_, session) = await StaffAsync("sales");
        var eventsBefore = StatusEventCount(order.Code);

        var response = await CancelAsync(order.Code, session, new { reason = "Muốn hủy" });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        Assert.Equal(8, await _kit.StockOfAsync(order.SkuId));
        Assert.DoesNotContain(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Cancelled);
        Assert.Empty(await AuditOfAsync(order.Code, "commerce.order.cancel"));
        Assert.Equal(eventsBefore, StatusEventCount(order.Code));
    }

    [Fact]
    // Đơn đang giao nhưng không có vận đơn còn hiệu lực thì nhân viên hủy được.
    public async Task Cancel_ShippingOrderWithoutShipment_Succeeds()
    {
        var order = await PlaceOrderAsync("cod", quantity: 2, stock: 10);
        await SetOrderStateAsync(order.Code, OrderStatus.Shipping);
        var (_, session) = await StaffAsync("sales");

        var response = await CancelAsync(order.Code, session, new { reason = "Vận đơn đã hủy" });

        var data = await ReadDataAsync(response);
        Assert.Equal("cancelled", data.GetProperty("status").GetString());
        Assert.Equal(10, await _kit.StockOfAsync(order.SkuId));
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Returned)]
    [InlineData(OrderStatus.Cancelled)]
    // Đơn đã giao, đã hoàn hoặc đã hủy không hủy được nữa.
    public async Task Cancel_FromTerminalStatus_Returns409(OrderStatus status)
    {
        var order = await PlaceOrderAsync("cod", quantity: 2, stock: 10);
        await SetOrderStateAsync(order.Code, status);
        var (_, session) = await StaffAsync("sales");

        var response = await CancelAsync(order.Code, session, new { reason = "Thử hủy" });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        Assert.Equal(8, await _kit.StockOfAsync(order.SkuId));
        Assert.Empty(await AuditOfAsync(order.Code, "commerce.order.cancel"));
    }

    [Fact]
    public async Task Cancel_UnknownCode_Returns404()
    {
        var (_, session) = await StaffAsync("sales");

        var response = await CancelAsync("PK00000000", session, new { reason = "Thử" });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "ORDER_NOT_FOUND");
    }

    [Fact]
    // `support` không có `orders:cancel` nên nhận 403, và đơn không đổi.
    public async Task Cancel_WithoutPermission_IsRejectedAndOrderUnchanged()
    {
        var order = await PlaceOrderAsync("cod", quantity: 2, stock: 10);
        var (_, supportSession) = await StaffAsync("support");

        await AssertErrorAsync(
            await CancelAsync(order.Code, supportSession, new { reason = "Thử" }), HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(HttpStatusCode.Unauthorized, (await CancelAsync(order.Code, null, new { reason = "Thử" })).StatusCode);
        Assert.Equal(8, await _kit.StockOfAsync(order.SkuId));
    }

    [Fact]
    // Hai yêu cầu hủy cùng đơn chạy cùng lúc, và hàng rào giữ cả hai tại bước trả kho nếu khóa dòng không chặn.
    // Có khóa dòng thì chỉ một yêu cầu tới bước trả kho, nên kho cộng một lần.
    public async Task Cancel_ConcurrentRequests_AreSerializedByRowLock()
    {
        var order = await PlaceOrderAsync("cod", quantity: 2, stock: 10);
        var (_, firstSession) = await StaffAsync("sales");
        var (_, secondSession) = await StaffAsync("sales");
        _factory.StockReleaseGate.Arm(participants: 2);

        var responses = await Task.WhenAll(
            CancelAsync(order.Code, firstSession, new { reason = "Yêu cầu một" }),
            CancelAsync(order.Code, secondSession, new { reason = "Yêu cầu hai" }));
        var releaseCalls = _factory.StockReleaseGate.ReleaseCalls;
        _factory.StockReleaseGate.Disarm();

        var statusCodes = responses.Select(response => response.StatusCode).OrderBy(code => (int)code).ToList();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statusCodes);
        Assert.Equal(1, releaseCalls);
        Assert.Equal(10, await _kit.StockOfAsync(order.SkuId));
        Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Cancelled);
        Assert.Single(await AuditOfAsync(order.Code, "commerce.order.cancel"));
        Assert.Single(
            _factory.DomainEvents.OfType<OrderStatusChangedEvent>(), e => e.Code == order.Code && e.Status == OrderStatus.Cancelled);
    }

    // ---------- Đánh dấu đã xuất hóa đơn ----------

    [Fact]
    // Đơn có thông tin hóa đơn thì đặt cờ và ghi một lịch sử cùng trạng thái.
    // Test cũng kiểm một audit và không có sự kiện đổi trạng thái.
    public async Task InvoiceIssuance_WithInvoiceInfo_SetsFlagAndRecordsOnce()
    {
        var order = await PlaceOrderAsync("cod", withInvoice: true);
        var (staffId, session) = await StaffAsync("sales");
        var eventsBefore = StatusEventCount(order.Code);

        var data = await ReadDataAsync(await IssueInvoiceAsync(order.Code, session));

        var flags = data.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()!).ToList();
        Assert.Contains("invoice_issued", flags);
        Assert.Equal("pending_confirmation", data.GetProperty("status").GetString());

        var history = (await HistoryOfAsync(order.Code)).Single(row => row.Reason == "Đã xuất hóa đơn VAT.");
        Assert.Equal(OrderStatus.PendingConfirmation, history.From);
        Assert.Equal(OrderStatus.PendingConfirmation, history.To);
        Assert.Equal(staffId, history.ActorId);

        var audit = Assert.Single(await AuditOfAsync(order.Code, "commerce.order.invoice_issue"));
        Assert.Equal(staffId, audit.ActorId);
        Assert.Equal(eventsBefore, StatusEventCount(order.Code));
    }

    [Fact]
    // PUT có tính lặp lại an toàn: lần hai trả 200 và không ghi thêm lịch sử hay audit.
    public async Task InvoiceIssuance_Repeated_IsIdempotent()
    {
        var order = await PlaceOrderAsync("cod", withInvoice: true);
        var (_, session) = await StaffAsync("sales");

        await ReadDataAsync(await IssueInvoiceAsync(order.Code, session));
        var second = await ReadDataAsync(await IssueInvoiceAsync(order.Code, session));

        var flags = second.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()!).ToList();
        Assert.Single(flags, flag => flag == "invoice_issued");
        Assert.Single(await HistoryOfAsync(order.Code), row => row.Reason == "Đã xuất hóa đơn VAT.");
        Assert.Single(await AuditOfAsync(order.Code, "commerce.order.invoice_issue"));
    }

    [Fact]
    // Đơn không có thông tin hóa đơn thì không có gì để đánh dấu.
    public async Task InvoiceIssuance_WithoutInvoiceInfo_Returns409()
    {
        var order = await PlaceOrderAsync("cod", withInvoice: false);
        var (_, session) = await StaffAsync("sales");

        var response = await IssueInvoiceAsync(order.Code, session);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVOICE_NOT_REQUESTED");
        Assert.Empty(await AuditOfAsync(order.Code, "commerce.order.invoice_issue"));
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Returned)]
    // Đơn đã hủy hoặc đã hoàn không xuất hóa đơn nữa.
    public async Task InvoiceIssuance_EndedOrder_Returns409(OrderStatus status)
    {
        var order = await PlaceOrderAsync("cod", withInvoice: true);
        await SetOrderStateAsync(order.Code, status);
        var (_, session) = await StaffAsync("sales");

        var response = await IssueInvoiceAsync(order.Code, session);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        Assert.Empty(await AuditOfAsync(order.Code, "commerce.order.invoice_issue"));
    }

    [Fact]
    public async Task InvoiceIssuance_UnknownCode_Returns404()
    {
        var (_, session) = await StaffAsync("sales");

        var response = await IssueInvoiceAsync("PK00000000", session);

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "ORDER_NOT_FOUND");
    }

    [Fact]
    // `support` không có `orders:update-status` nên nhận 403.
    public async Task InvoiceIssuance_WithoutPermission_IsRejected()
    {
        var order = await PlaceOrderAsync("cod", withInvoice: true);
        var (_, supportSession) = await StaffAsync("support");

        await AssertErrorAsync(
            await IssueInvoiceAsync(order.Code, supportSession), HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Empty(await AuditOfAsync(order.Code, "commerce.order.invoice_issue"));
    }

    [Fact]
    // Bộ lọc `flag=invoice_issued` của danh sách COM-14 tìm được đơn đã xuất hóa đơn.
    public async Task InvoiceIssuance_FlagIsFilterableInOrderList()
    {
        var issued = await PlaceOrderAsync("cod", withInvoice: true);
        var notIssued = await PlaceOrderAsync("cod", withInvoice: true);
        var (_, session) = await StaffAsync("sales");
        await ReadDataAsync(await IssueInvoiceAsync(issued.Code, session));

        var response = await SendAsync(HttpMethod.Get, $"{AdminOrdersPath}?flag=invoice_issued&limit=100", session);

        var data = await ReadDataAsync(response);
        var codes = data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToList();
        Assert.Contains(issued.Code, codes);
        Assert.DoesNotContain(notIssued.Code, codes);
    }
}
