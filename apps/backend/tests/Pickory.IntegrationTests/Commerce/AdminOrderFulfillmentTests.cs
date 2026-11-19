using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// Nhân viên bắt đầu giao hàng thủ công bằng `PUT /api/admin/orders/{code}/fulfillment`.
// Test đọc parser qua HTTP vì `UpdateFulfillmentRequest` là lớp `internal` của module.
[Collection(CommerceCollection.Name)]
public sealed class AdminOrderFulfillmentTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string AdminOrdersPath = "/api/admin/orders";
    private const string OrdersPath = "/api/orders";
    private const string WebOrigin = "http://localhost:5173";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";
    private const string AuditTargetType = "order";
    private const string FulfillmentAuditAction = "commerce.order.fulfillment_update";
    private const string FulfillmentFixture = "commerce/manual-fulfillment.json";
    private const string ManualShippingFlag = "manual_shipping";
    private const string InvalidReasonMessage = "Nhập lý do từ 1 đến 500 ký tự.";
    private const string UnknownFieldMessage = "Trường này không được hỗ trợ.";
    private const string InvalidToStatusMessage = "Chọn một trong các trạng thái: confirmed, shipping, delivered, returned.";
    private const string ValidReason = "GHN không nhận vận đơn. Shop tự giao.";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];
    private readonly List<string> _codes = [];

    public AdminOrderFulfillmentTests(CheckoutWebApplicationFactory factory, CommerceDatabaseFixture database)
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
        _factory.GhnClient.ClearFailures();
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public async Task DisposeAsync()
    {
        await using (var identity = _database.CreateIdentityContext())
        {
            await identity.AuditLogs
                .Where(log => log.TargetType == AuditTargetType && _codes.Contains(log.TargetId))
                .ExecuteDeleteAsync();
        }

        await using (var shipping = _database.CreateShippingContext())
        {
            var shipmentIds = await shipping.Shipments
                .Where(shipment => _codes.Any(code => shipment.Ref == code || shipment.Ref.StartsWith(code + "#")))
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

    // Đặt đơn bằng route thật của khách để có kho đã giữ, rồi đưa đơn về trạng thái cần thử.
    private async Task<PlacedOrder> PlaceOrderAsync(
        OrderStatus status = OrderStatus.Confirmed,
        PaymentStatus? paymentStatus = null,
        IReadOnlyList<string>? flags = null)
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = await _kit.AddProductAsync(price: 100_000, stock: 10);
        var skuId = product.SkuIds[0];
        var addResponse = await _kit.AddAsync(anonymousId, skuId, 2);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var previewBody = new
        {
            address = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            paymentMethod = "cod",
        };
        var previewResponse = await _kit.SendAsync(HttpMethod.Post, "/api/checkout/previews", anonymousId, previewBody);
        var previewData = await CartTestKit.ReadCartAsync(previewResponse);
        var total = previewData.GetProperty("total").GetInt32();

        var orderBody = new Dictionary<string, object?>
        {
            ["contact"] = new { name = "Nguyễn Văn An", phone = "0901234567", email = "an@example.com" },
            ["address"] = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            ["paymentMethod"] = "cod",
            ["expectedTotal"] = total,
        };

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

        await SetOrderStateAsync(code, status, paymentStatus, flags);
        return new PlacedOrder(code, skuId);
    }

    private async Task SetOrderStateAsync(
        string code, OrderStatus status, PaymentStatus? paymentStatus, IReadOnlyList<string>? flags)
    {
        await using var commerce = _database.CreateCommerceContext();
        var order = await commerce.Orders.SingleAsync(row => row.Code == code);
        order.Status = status;
        if (paymentStatus is { } newPaymentStatus)
        {
            order.PaymentStatus = newPaymentStatus;
        }

        if (flags is not null)
        {
            order.Flags = flags.ToList();
        }

        await commerce.SaveChangesAsync();
    }

    private async Task<(string UserId, string Session)> StaffAsync(string roleCode)
    {
        var (user, session) = await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com29");
        return (user.Id, session);
    }

    // `body` là chuỗi thì gửi nguyên văn như JSON thô, để test gửi được kiểu dữ liệu sai.
    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? session, object? body = null)
    {
        var cookie = session is null ? null : $"pk_session={session}";
        if (body is not string rawJson)
        {
            return _client.SendAsync(ActorHttp.CreateJsonRequest(method, path, body, cookie));
        }

        var request = ActorHttp.CreateJsonRequest(method, path, null, cookie);
        request.Content = new StringContent(rawJson, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> FulfillAsync(string code, string? session, object? body) =>
        SendAsync(HttpMethod.Put, $"{AdminOrdersPath}/{code}/fulfillment", session, body);

    private Task<HttpResponseMessage> FulfillAsync(string code, string? session, string toStatus, string reason = ValidReason) =>
        FulfillAsync(code, session, new { toStatus, reason });

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, raw);
        using var document = JsonDocument.Parse(raw);
        var error = document.RootElement.GetProperty("error").Clone();
        Assert.Equal(code, error.GetProperty("code").GetString());
        return error;
    }

    // Mỗi phần tử của `details` là một cặp khóa và thông báo. Test so cả hai để bắt lỗi gắn sai trường.
    private static List<(string Key, string Message)> DetailsOf(JsonElement error) =>
        error.GetProperty("details").EnumerateArray()
            .Select(detail => (detail.GetProperty("key").GetString()!, detail.GetProperty("message").GetString()!))
            .ToList();

    private sealed record OrderSnapshot(OrderStatus Status, PaymentStatus PaymentStatus, List<string> Flags);

    private async Task<OrderSnapshot> SnapshotOfAsync(string code)
    {
        await using var commerce = _database.CreateCommerceContext();
        var order = await commerce.Orders.AsNoTracking().SingleAsync(row => row.Code == code);
        return new OrderSnapshot(order.Status, order.PaymentStatus, order.Flags.ToList());
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

    private async Task<List<Pickory.Modules.Identity.Entities.AuditLog>> AuditOfAsync(string code)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.AsNoTracking()
            .Where(log => log.TargetType == AuditTargetType && log.TargetId == code && log.Action == FulfillmentAuditAction)
            .ToListAsync();
    }

    private int StatusEventCount(string code) =>
        _factory.DomainEvents.OfType<OrderStatusChangedEvent>().Count(e => e.Code == code);

    private async Task<Shipment> CreateShipmentDirectAsync(string code)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IShippingService>()
            .CreateShipmentAsync(ShipmentTestSetup.CreateRequest(code), CancellationToken.None);
    }

    // Mọi đường lỗi và đường lặp lại phải để nguyên đơn, lịch sử, audit và sự kiện.
    private sealed record Baseline(OrderSnapshot Order, int HistoryCount, int EventCount);

    private async Task<Baseline> BaselineOfAsync(string code) =>
        new(await SnapshotOfAsync(code), (await HistoryOfAsync(code)).Count, StatusEventCount(code));

    private async Task AssertUnchangedAsync(string code, Baseline baseline)
    {
        Assert.Equal(baseline.Order, await SnapshotOfAsync(code), new OrderSnapshotComparer());
        Assert.Equal(baseline.HistoryCount, (await HistoryOfAsync(code)).Count);
        Assert.Empty(await AuditOfAsync(code));
        Assert.Equal(baseline.EventCount, StatusEventCount(code));
    }

    private sealed class OrderSnapshotComparer : IEqualityComparer<OrderSnapshot>
    {
        public bool Equals(OrderSnapshot? x, OrderSnapshot? y) =>
            x is not null && y is not null
            && x.Status == y.Status
            && x.PaymentStatus == y.PaymentStatus
            && x.Flags.SequenceEqual(y.Flags);

        public int GetHashCode(OrderSnapshot obj) => HashCode.Combine(obj.Status, obj.PaymentStatus);
    }

    // ---------- Thành công ----------

    [Fact]
    // Đơn `confirmed` sang `shipping`: thêm cờ, giữ cờ cũ, ghi lịch sử với người làm và lý do đã cắt khoảng trắng.
    public async Task StartManualShipping_FromConfirmed_UpdatesStatusFlagAndHistory()
    {
        var order = await PlaceOrderAsync(flags: ["late_payment"]);
        var (staffId, session) = await StaffAsync("sales");

        var response = await FulfillAsync(order.Code, session, "shipping", "  " + ValidReason + "  ");

        var data = await ReadDataAsync(response);
        Assert.Equal("shipping", data.GetProperty("status").GetString());
        Assert.Equal(["late_payment", ManualShippingFlag], data.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var stored = await SnapshotOfAsync(order.Code);
        Assert.Equal(OrderStatus.Shipping, stored.Status);
        Assert.Equal(["late_payment", ManualShippingFlag], stored.Flags);

        var history = Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Shipping);
        Assert.Equal(OrderStatus.Confirmed, history.From);
        Assert.Equal(staffId, history.ActorId);
        Assert.Equal(ValidReason, history.Reason);
    }

    [Fact]
    // Audit ghi trạng thái và cờ trước sau, kèm lý do. Sự kiện đổi trạng thái phát đúng một lần với trạng thái cũ.
    public async Task StartManualShipping_WritesAuditAndEventOnce()
    {
        var order = await PlaceOrderAsync(flags: ["late_payment"]);
        var (staffId, session) = await StaffAsync("sales");

        await ReadDataAsync(await FulfillAsync(order.Code, session, "shipping"));

        var audit = Assert.Single(await AuditOfAsync(order.Code));
        Assert.Equal(staffId, audit.ActorId);
        var before = audit.Before!.RootElement;
        var after = audit.After!.RootElement;
        // Audit chỉ có nhãn trạng thái, cờ và lý do. Cột `jsonb` không giữ thứ tự khóa, nên test sắp tên khóa trước khi so.
        Assert.Equal(["flags", "status"], before.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(["flags", "reason", "status"], after.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("confirmed", before.GetProperty("status").GetString());
        Assert.Equal(["late_payment"], before.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()));
        Assert.Equal("shipping", after.GetProperty("status").GetString());
        Assert.Equal(
            ["late_payment", ManualShippingFlag], after.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()));
        Assert.Equal(ValidReason, after.GetProperty("reason").GetString());

        var statusEvent = Assert.Single(
            _factory.DomainEvents.OfType<OrderStatusChangedEvent>(), e => e.Code == order.Code && e.Status == OrderStatus.Shipping);
        Assert.Equal(OrderStatus.Confirmed, statusEvent.PreviousStatus);
    }

    [Fact]
    // Luồng thủ công không gọi GHN, không tạo vận đơn, không đổi tồn kho và không đổi thanh toán.
    public async Task StartManualShipping_DoesNotTouchGhnStockOrPayment()
    {
        var order = await PlaceOrderAsync(paymentStatus: PaymentStatus.Unpaid);
        var (_, session) = await StaffAsync("sales");
        var stockBefore = await _kit.StockOfAsync(order.SkuId);
        var movementsBefore = await _kit.MovementCountOfAsync(order.SkuId);
        var ghnCallsBefore = _factory.Recorder.TotalCallCount;

        await ReadDataAsync(await FulfillAsync(order.Code, session, "shipping"));

        Assert.Equal(ghnCallsBefore, _factory.Recorder.TotalCallCount);
        Assert.DoesNotContain(_factory.Recorder.CreateOrderRequests, request => request.ClientOrderCode == order.Code);
        Assert.Empty(_factory.Recorder.CancelledOrderCodes);
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IShippingService>()
            .GetShipmentByRefAsync(order.Code, CancellationToken.None));
        Assert.Equal(stockBefore, await _kit.StockOfAsync(order.SkuId));
        Assert.Equal(movementsBefore, await _kit.MovementCountOfAsync(order.SkuId));
        Assert.Equal(PaymentStatus.Unpaid, (await SnapshotOfAsync(order.Code)).PaymentStatus);
    }

    [Fact]
    // Response thành công khớp hình dạng ví dụ `start manual shipping` của hợp đồng.
    public async Task StartManualShipping_MatchesContractFixture()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");

        var response = await FulfillAsync(order.Code, session, "shipping");

        await AssertMatchesFixtureIgnoringOptionalKeysAsync("start manual shipping", response);
    }

    // Fixture chỉ ghi khóa bắt buộc, còn response thật có thêm các khóa tùy chọn có từ trước.
    // Hàm bỏ đúng các khóa đó khỏi response, nên khóa thừa hay thiếu khác vẫn làm test đỏ.
    private static async Task AssertMatchesFixtureIgnoringOptionalKeysAsync(string exampleName, HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var data = body["data"]!;
        data["contact"]!.AsObject().Remove("note");
        data["shippingAddress"]!.AsObject().Remove("provinceCode");
        data["shippingAddress"]!.AsObject().Remove("wardCode");
        foreach (var item in data["items"]!.AsArray())
        {
            item!.AsObject().Remove("imageUrl");
            item.AsObject().Remove("imagePublicId");
        }

        using var filtered = new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        await ContractFixture.AssertMatches(FulfillmentFixture, exampleName, filtered);
    }

    // ---------- Body sai ----------

    public static TheoryData<string, string[]> InvalidBodies => new()
    {
        { "null", ["toStatus", "reason"] },
        { "[]", ["toStatus", "reason"] },
        { "\"shipping\"", ["toStatus", "reason"] },
        { "{}", ["toStatus", "reason"] },
        { "{\"toStatus\":\"shipping\",\"reason\":\"Lý do\",\"extra\":1}", ["extra"] },
        { "{\"toStatus\":\"shipping\"}", ["reason"] },
        { "{\"reason\":\"Lý do\"}", ["toStatus"] },
        { "{\"toStatus\":1,\"reason\":\"Lý do\"}", ["toStatus"] },
        { "{\"toStatus\":null,\"reason\":\"Lý do\"}", ["toStatus"] },
        { "{\"toStatus\":\"shipping\",\"reason\":5}", ["reason"] },
        { "{\"toStatus\":\"shipping\",\"reason\":null}", ["reason"] },
        { "{\"toStatus\":\"cancelled\",\"reason\":\"Lý do\"}", ["toStatus"] },
        { "{\"toStatus\":\"pending_payment\",\"reason\":\"Lý do\"}", ["toStatus"] },
        { "{\"toStatus\":\"pending_confirmation\",\"reason\":\"Lý do\"}", ["toStatus"] },
        { "{\"toStatus\":\"Shipping\",\"reason\":\"Lý do\"}", ["toStatus"] },
        { "{\"toStatus\":\"shipping\",\"reason\":\"\"}", ["reason"] },
        { "{\"toStatus\":\"shipping\",\"reason\":\"   \"}", ["reason"] },
        { "{\"toStatus\":\"khong_co\",\"reason\":\" \"}", ["toStatus", "reason"] },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    // Body sai trả 400 với một dòng `details` cho mỗi trường lỗi, và không ghi gì vào đơn.
    public async Task Fulfill_InvalidBody_Returns400WithOneDetailPerField(string rawBody, string[] expectedKeys)
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");
        var baseline = await BaselineOfAsync(order.Code);

        // Chuỗi `null` nghĩa là không gửi body.
        object? body = rawBody == "null" ? null : rawBody;
        var error = await AssertErrorAsync(await FulfillAsync(order.Code, session, body), HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        Assert.Equal(expectedKeys.Order(), DetailsOf(error).Select(detail => detail.Key).Order());
        await AssertUnchangedAsync(order.Code, baseline);
    }

    [Fact]
    // Request sai bị chặn trước khi tra đơn: mã không tồn tại vẫn trả 400, không phải 404.
    public async Task Fulfill_InvalidBody_IsRejectedBeforeOrderLookup()
    {
        var (_, session) = await StaffAsync("sales");

        var response = await FulfillAsync("PKKHONGCO", session, "shipping", reason: "   ");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    // Thông báo lỗi `reason` và `toStatus` đúng chữ trong hợp đồng, và trường lạ gắn đúng tên trường.
    public async Task Fulfill_InvalidBody_UsesContractMessages()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");

        var reasonError = await AssertErrorAsync(
            await FulfillAsync(order.Code, session, "shipping", reason: " "), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var unknownError = await AssertErrorAsync(
            await FulfillAsync(order.Code, session, (object)"{\"toStatus\":\"shipping\",\"reason\":\"x\",\"note\":\"y\"}"),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var toStatusError = await AssertErrorAsync(
            await FulfillAsync(order.Code, session, "cancelled"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        Assert.Equal([("reason", InvalidReasonMessage)], DetailsOf(reasonError));
        Assert.Equal([("note", UnknownFieldMessage)], DetailsOf(unknownError));
        Assert.Equal([("toStatus", InvalidToStatusMessage)], DetailsOf(toStatusError));
    }

    [Fact]
    // Response lỗi của lý do rỗng khớp ví dụ `invalid reason` của hợp đồng.
    public async Task Fulfill_BlankReason_MatchesContractFixture()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");

        var response = await FulfillAsync(order.Code, session, "shipping", reason: " ");

        await ContractFixture.AssertMatches(FulfillmentFixture, "invalid reason", response);
    }

    [Fact]
    // Đúng 500 ký tự thì nhận, và khoảng trắng hai đầu không tính vào giới hạn. Lịch sử lưu lý do đã cắt.
    public async Task Fulfill_ReasonOfExactly500Characters_IsAccepted()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");
        var reason = new string('a', 500);

        await ReadDataAsync(await FulfillAsync(order.Code, session, "shipping", "  " + reason + "  "));

        Assert.Equal(reason, Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Shipping).Reason);
    }

    [Fact]
    // 501 ký tự sau khi cắt khoảng trắng bị từ chối và không ghi gì.
    public async Task Fulfill_ReasonOf501Characters_Returns400()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");
        var baseline = await BaselineOfAsync(order.Code);

        var error = await AssertErrorAsync(
            await FulfillAsync(order.Code, session, "shipping", new string('a', 501)),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        Assert.Equal([("reason", InvalidReasonMessage)], DetailsOf(error));
        await AssertUnchangedAsync(order.Code, baseline);
    }

    // ---------- Chuyển đổi không hợp lệ ----------

    [Theory]
    [InlineData(OrderStatus.PendingPayment, "shipping")]
    [InlineData(OrderStatus.PendingConfirmation, "shipping")]
    [InlineData(OrderStatus.Delivered, "shipping")]
    [InlineData(OrderStatus.Returned, "shipping")]
    [InlineData(OrderStatus.Cancelled, "shipping")]
    [InlineData(OrderStatus.Confirmed, "delivered")]
    [InlineData(OrderStatus.Confirmed, "returned")]
    // Cặp ngoài bảng trả 409 và không ghi gì, gồm cả hai cặp `confirmed` sang `delivered` và `returned`.
    public async Task Fulfill_PairOutsideTable_Returns409AndWritesNothing(OrderStatus current, string toStatus)
    {
        var order = await PlaceOrderAsync(current);
        var (_, session) = await StaffAsync("sales");
        var baseline = await BaselineOfAsync(order.Code);

        var response = await FulfillAsync(order.Code, session, toStatus);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        await AssertUnchangedAsync(order.Code, baseline);
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("delivered")]
    [InlineData("returned")]
    // Ba cặp xuất phát từ `shipping` thuộc phần chưa mở của route, nên hiện trả 409 và không ghi gì.
    public async Task Fulfill_FromShippingToOtherStatus_Returns409AndWritesNothing(string toStatus)
    {
        var order = await PlaceOrderAsync(OrderStatus.Shipping, flags: [ManualShippingFlag]);
        var (_, session) = await StaffAsync("sales");
        var baseline = await BaselineOfAsync(order.Code);

        var response = await FulfillAsync(order.Code, session, toStatus);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        await AssertUnchangedAsync(order.Code, baseline);
    }

    [Fact]
    // Response lỗi chuyển đổi khớp ví dụ `invalid transition` của hợp đồng, gồm cả thông báo.
    public async Task Fulfill_InvalidTransition_MatchesContractFixture()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");

        var response = await FulfillAsync(order.Code, session, "delivered", "Bỏ qua bước giao hàng.");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Không thể chuyển trạng thái giao hàng theo yêu cầu.",
            document.RootElement.GetProperty("error").GetProperty("message").GetString());
        await ContractFixture.AssertMatches(FulfillmentFixture, "invalid transition", response);
    }

    [Fact]
    // Đơn còn vận đơn GHN hiệu lực trả 409 và không ghi gì. Vận đơn giữ nguyên.
    public async Task Fulfill_WithActiveGhnShipment_Returns409AndWritesNothing()
    {
        var order = await PlaceOrderAsync();
        var shipment = await CreateShipmentDirectAsync(order.Code);
        var (_, session) = await StaffAsync("sales");
        var baseline = await BaselineOfAsync(order.Code);

        var response = await FulfillAsync(order.Code, session, "shipping");

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        await AssertUnchangedAsync(order.Code, baseline);
        await using var scope = _factory.Services.CreateAsyncScope();
        var current = await scope.ServiceProvider.GetRequiredService<IShippingService>()
            .GetShipmentByRefAsync(order.Code, CancellationToken.None);
        Assert.Equal(shipment.Id, current?.Id);
    }

    [Fact]
    // Vận đơn GHN đã hủy không còn hiệu lực. Đơn `confirmed` vẫn chuyển sang giao thủ công được.
    public async Task Fulfill_WithCancelledGhnShipment_StartsManualShipping()
    {
        var order = await PlaceOrderAsync();
        var shipment = await CreateShipmentDirectAsync(order.Code);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var shippingService = scope.ServiceProvider.GetRequiredService<IShippingService>();
            await shippingService.CancelShipmentAsync(shipment.Id, CancellationToken.None);
            // Điều kiện đầu vào của test: vận đơn đã hủy thì tra theo mã đơn không còn thấy.
            Assert.Null(await shippingService.GetShipmentByRefAsync(order.Code, CancellationToken.None));
        }

        var (_, session) = await StaffAsync("sales");

        var data = await ReadDataAsync(await FulfillAsync(order.Code, session, "shipping"));

        Assert.Equal("shipping", data.GetProperty("status").GetString());
        Assert.Equal([ManualShippingFlag], (await SnapshotOfAsync(order.Code)).Flags);
    }

    [Fact]
    // Đơn `confirmed` đã có sẵn cờ `manual_shipping` vẫn chuyển được. Cờ không bị thêm lần hai.
    public async Task Fulfill_ConfirmedOrderAlreadyHavingFlag_KeepsSingleFlag()
    {
        var order = await PlaceOrderAsync(flags: [ManualShippingFlag]);
        var (_, session) = await StaffAsync("sales");

        var data = await ReadDataAsync(await FulfillAsync(order.Code, session, "shipping"));

        Assert.Equal([ManualShippingFlag], data.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()));
        Assert.Equal([ManualShippingFlag], (await SnapshotOfAsync(order.Code)).Flags);
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment, "pending_payment")]
    [InlineData(OrderStatus.PendingConfirmation, "pending_confirmation")]
    [InlineData(OrderStatus.Cancelled, "cancelled")]
    // Ba trạng thái ngoài hợp đồng bị từ chối bằng 400. Đơn đang ở đúng trạng thái đó cũng không rơi vào nhánh cùng trạng thái.
    public async Task Fulfill_StatusOutsideContract_Returns400EvenWhenOrderIsInThatStatus(OrderStatus current, string toStatus)
    {
        var order = await PlaceOrderAsync(current);
        var (_, session) = await StaffAsync("sales");
        var baseline = await BaselineOfAsync(order.Code);

        var error = await AssertErrorAsync(
            await FulfillAsync(order.Code, session, toStatus), HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        Assert.Equal(["toStatus"], DetailsOf(error).Select(detail => detail.Key));
        await AssertUnchangedAsync(order.Code, baseline);
    }

    // ---------- Cùng trạng thái ----------

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipping)]
    [InlineData(OrderStatus.Delivered)]
    // Request tới trạng thái hiện tại trả 200 và không thêm lịch sử, audit, sự kiện hay cờ.
    public async Task Fulfill_ToCurrentStatus_Returns200AndWritesNothing(OrderStatus current)
    {
        var order = await PlaceOrderAsync(current);
        var (_, session) = await StaffAsync("sales");
        var baseline = await BaselineOfAsync(order.Code);
        var toStatus = JsonNamingPolicy.SnakeCaseLower.ConvertName(current.ToString());

        var data = await ReadDataAsync(await FulfillAsync(order.Code, session, toStatus));

        Assert.Equal(toStatus, data.GetProperty("status").GetString());
        await AssertUnchangedAsync(order.Code, baseline);
    }

    [Fact]
    // Hai request cùng đơn đến khi dòng đơn đang bị giữ khóa. Khi nhả khóa, chúng xếp hàng và chỉ request đầu ghi.
    public async Task Fulfill_ConcurrentRequests_WriteOneHistoryAuditAndEvent()
    {
        var order = await PlaceOrderAsync();
        // Hai nhân viên khác nhau gọi cùng lúc, để hai request không dùng chung một phiên đăng nhập.
        var (_, firstSession) = await StaffAsync("sales");
        var (_, secondSession) = await StaffAsync("sales");

        // Giữ khóa dòng đơn bằng một transaction riêng để cả hai request chắc chắn chạy chồng lên nhau.
        // Nếu service không khóa dòng, cả hai request đọc được trạng thái `confirmed` cũ trong lúc chờ.
        await using var holder = _database.CreateCommerceContext();
        await using var holdTransaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlInterpolatedAsync($"""SELECT "id" FROM orders WHERE "code" = {order.Code} FOR UPDATE""");
        var pending = new[] { firstSession, secondSession }
            .Select(session => Task.Run(() => FulfillAsync(order.Code, session, "shipping")))
            .ToList();

        // Chờ tối đa 15 giây cho tới khi cả hai request đứng chờ khóa trong DB. Hết hạn thì hàm trả về mà test không đỏ.
        await WaitForLockWaitersAsync(holder, expectedWaiters: 2);
        await holdTransaction.CommitAsync();
        var responses = await Task.WhenAll(pending);

        foreach (var response in responses)
        {
            await ReadDataAsync(response);
        }

        Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Shipping);
        Assert.Single(await AuditOfAsync(order.Code));
        Assert.Equal(1, StatusEventCount(order.Code));
        Assert.Equal([ManualShippingFlag], (await SnapshotOfAsync(order.Code)).Flags);
    }

    // Hỏi `pg_stat_activity` để đếm phiên đang chờ khóa. Hết hạn thì hàm trả về mà test không đỏ.
    private static async Task WaitForLockWaitersAsync(CommerceDbContext holder, int expectedWaiters)
    {
        // Giới hạn 15 giây để test không treo khi request không bao giờ đứng chờ khóa.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            // Chỉ đếm phiên có kiểu chờ Lock. Request đứng sau khóa dòng đơn thuộc nhóm này.
            var waiters = await holder.Database
                .SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()""")
                .SingleAsync();
            if (waiters >= expectedWaiters)
            {
                // Đủ số phiên chờ nghĩa là cả hai request đã tới điểm đọc đơn. Lúc này mới nhả khóa.
                return;
            }

            // Chờ ngắn giữa hai lần hỏi. Cách này nhanh hơn chờ cố định khi request tới sớm.
            await Task.Delay(50);
        }
    }

    [Fact]
    // Ghi audit lỗi thì transaction quay lui: đơn, lịch sử và cờ giữ nguyên, và không có sự kiện nào được phát.
    public async Task Fulfill_AuditWriteFails_RollsBackAndEmitsNoEvent()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");
        var baseline = await BaselineOfAsync(order.Code);

        using var failingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditLogWriter>();
                services.AddSingleton<IAuditLogWriter, FailingAuditLogWriter>();
            });
        });
        using var failingClient = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var recorded = failingFactory.Services.GetRequiredService<RecordedDomainEvents>();

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put, $"{AdminOrdersPath}/{order.Code}/fulfillment", new { toStatus = "shipping", reason = ValidReason }, $"pk_session={session}");
        var response = await failingClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertUnchangedAsync(order.Code, baseline);
        Assert.DoesNotContain(recorded.OfType<OrderStatusChangedEvent>(), e => e.Code == order.Code);
    }

    [Fact]
    // Bên nghe sự kiện đọc DB bằng kết nối riêng. Nó phải thấy đơn đã sang `shipping`, nên sự kiện chỉ phát sau commit.
    public async Task Fulfill_EventHandlerSeesCommittedState()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("sales");
        var observation = new CommittedStateObservation(order.Code);

        using var observingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(observation);
                services.AddSingleton(_database);
                services.AddSingleton<IDomainEventHandler<OrderStatusChangedEvent>, CommittedStateObserver>();
            });
        });
        using var observingClient = observingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put, $"{AdminOrdersPath}/{order.Code}/fulfillment", new { toStatus = "shipping", reason = ValidReason }, $"pk_session={session}");
        var response = await observingClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Shipping, observation.StatusWhenHandled);
    }

    private sealed class CommittedStateObservation(string code)
    {
        public string Code { get; } = code;

        // Trạng thái đơn mà bên nghe đọc được lúc nhận sự kiện của đơn đang kiểm.
        public OrderStatus? StatusWhenHandled { get; set; }
    }

    private sealed class CommittedStateObserver(
        CommittedStateObservation observation, IntegrationDatabaseFixture database)
        : IDomainEventHandler<OrderStatusChangedEvent>
    {
        public async Task HandleAsync(OrderStatusChangedEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event.Code != observation.Code)
            {
                return;
            }

            // Kết nối mới không thấy dữ liệu chưa commit của transaction đang chạy.
            await using var commerce = database.CreateCommerceContext();
            observation.StatusWhenHandled = await commerce.Orders.AsNoTracking()
                .Where(row => row.Code == @event.Code).Select(row => row.Status).SingleAsync(cancellationToken);
        }
    }

    private sealed class FailingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken)
        {
            // Cố ý ném ngoại lệ để kiểm tra transaction quay lui cùng thao tác chính.
            throw new InvalidOperationException("Lỗi cố ý từ nhật ký kiểm toán");
        }
    }

    // ---------- Quyền và đơn không tồn tại ----------

    [Fact]
    // Chưa đăng nhập nhận 401 và đơn không đổi.
    public async Task Fulfill_WithoutSession_Returns401()
    {
        var order = await PlaceOrderAsync();
        var baseline = await BaselineOfAsync(order.Code);

        var response = await FulfillAsync(order.Code, null, new { toStatus = "delivered", reason = "Khách chưa đăng nhập." });

        await ContractFixture.AssertMatches(FulfillmentFixture, "unauthorized", response);
        await AssertUnchangedAsync(order.Code, baseline);
    }

    [Fact]
    // Role `support` không có `orders:fulfill` nên nhận 403, kể cả khi body hợp lệ. Đơn không đổi.
    public async Task Fulfill_AsSupport_Returns403()
    {
        var order = await PlaceOrderAsync();
        var (_, session) = await StaffAsync("support");
        var baseline = await BaselineOfAsync(order.Code);

        var response = await FulfillAsync(order.Code, session, "shipping");

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertUnchangedAsync(order.Code, baseline);
    }

    [Fact]
    // Mã đơn không tồn tại nhận 404, khớp ví dụ `order not found`.
    public async Task Fulfill_UnknownCode_Returns404()
    {
        var (_, session) = await StaffAsync("sales");

        var response = await FulfillAsync("PKMISSINGQ", session, "delivered", "Đơn không tồn tại.");

        await ContractFixture.AssertMatches(FulfillmentFixture, "order not found", response);
    }
}
