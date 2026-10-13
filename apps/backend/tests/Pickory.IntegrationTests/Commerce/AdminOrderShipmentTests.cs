using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Modules.Shipping.Shipments;
using Pickory.Modules.Shipping.Sync;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-16: nhân viên tạo và hủy vận đơn GHN cho đơn, và đơn đổi trạng thái theo sự kiện giao hàng của shipping.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminOrderShipmentTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string AdminOrdersPath = "/api/admin/orders";
    private const string OrdersPath = "/api/orders";
    private const string WebOrigin = "http://localhost:5173";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";
    private const string AuditTargetType = "order";
    private const string CreateAuditAction = "commerce.order.shipment_create";
    private const string CancelAuditAction = "commerce.order.shipment_cancel";

    // Viết thẳng chuỗi cờ thay vì dùng hằng của handler, để đổi tên cờ không âm thầm qua test.
    private const string HandlerFlag = "shipment_cancelled";

    // Seed catalog đặt cân nặng 300 g và kích thước 20 x 10 x 2 cm cho SKU đầu tiên.
    private const int SeedUnitWeightGrams = 300;

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];
    private readonly List<string> _codes = [];

    public AdminOrderShipmentTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
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
        _factory.Recorder.AfterCreateOrder = null;
        _factory.GhnClient.ClearFailures();

        await using (var identity = _database.CreateIdentityContext())
        {
            await identity.AuditLogs
                .Where(log => log.TargetType == AuditTargetType && _codes.Contains(log.TargetId))
                .ExecuteDeleteAsync();
        }

        await using (var shipping = _database.CreateShippingContext())
        {
            // Vận đơn đã hủy có `ref` dạng `{ref}#cancelled#{n}`, nên dọn theo tiền tố của mã đơn.
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

    private sealed record PlacedOrder(string Code, string SkuId, int Total, int Subtotal);

    // Đặt đơn bằng route thật của khách rồi đưa về trạng thái cần thử. Bản chụp liên hệ và địa chỉ đúng như môi trường chạy.
    private async Task<PlacedOrder> PlaceOrderAsync(
        string paymentMethod = "cod",
        int quantity = 2,
        OrderStatus status = OrderStatus.Confirmed,
        PaymentStatus? paymentStatus = null)
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = await _kit.AddProductAsync(price: 100_000, stock: 10);
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
        var subtotal = previewData.GetProperty("subtotal").GetInt32();

        var orderBody = new Dictionary<string, object?>
        {
            ["contact"] = new { name = "Nguyễn Văn An", phone = "0901234567", email = "an@example.com" },
            ["address"] = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            ["paymentMethod"] = paymentMethod,
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

        await SetOrderStateAsync(code, status, paymentStatus);
        return new PlacedOrder(code, skuId, total, subtotal);
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
        var (user, session) = await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com16");
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

    private Task<HttpResponseMessage> CreateShipmentAsync(string code, string? session, object? body = null) =>
        SendAsync(HttpMethod.Post, $"{AdminOrdersPath}/{code}/shipment", session, body);

    private Task<HttpResponseMessage> CancelShipmentAsync(string code, string? session) =>
        SendAsync(HttpMethod.Delete, $"{AdminOrdersPath}/{code}/shipment", session);

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ActorHttp.ReadJsonAsync(response);
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

    private async Task<OrderStatus> StatusOfAsync(string code)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().Where(order => order.Code == code).Select(order => order.Status).SingleAsync();
    }

    private async Task<PaymentStatus> PaymentStatusOfAsync(string code)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().Where(order => order.Code == code).Select(order => order.PaymentStatus).SingleAsync();
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

    private async Task<Shipment?> CurrentShipmentAsync(string code)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IShippingService>()
            .GetShipmentByRefAsync(code, CancellationToken.None);
    }

    // Tạo vận đơn thẳng qua module shipping, để test của handler không phụ thuộc route tạo vận đơn.
    private async Task<Shipment> CreateShipmentDirectAsync(string code)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IShippingService>()
            .CreateShipmentAsync(ShipmentTestSetup.CreateRequest(code), CancellationToken.None);
    }

    private int StatusEventCount(string code) =>
        _factory.DomainEvents.OfType<OrderStatusChangedEvent>().Count(e => e.Code == code);

    private int GhnOrderCountFor(string code) =>
        _factory.Recorder.CreateOrderRequests.Count(request => request.ClientOrderCode == code);

    private static object Parcel(object weightGrams, object lengthCm, object widthCm, object heightCm) =>
        new { parcel = new { weightGrams, lengthCm, widthCm, heightCm } };

    // ---------- Tạo vận đơn ----------

    [Fact]
    // Đơn COD `confirmed`: GHN nhận `Ref` là mã đơn, thu hộ bằng tổng tiền, khai giá bằng tạm tính và kiện dựng từ dòng hàng.
    public async Task CreateShipment_CodOrder_SendsCodAmountAndMovesOrderToShipping()
    {
        var order = await PlaceOrderAsync("cod", quantity: 2);
        var (staffId, session) = await StaffAsync("sales");

        var response = await CreateShipmentAsync(order.Code, session);

        var data = await ReadDataAsync(response);
        Assert.Equal("shipping", data.GetProperty("status").GetString());
        var request = Assert.Single(_factory.Recorder.CreateOrderRequests, r => r.ClientOrderCode == order.Code);
        Assert.Equal(order.Total, request.CodAmount);
        Assert.Equal(order.Subtotal, request.InsuranceValue);
        Assert.Equal(SeedUnitWeightGrams * 2, request.Weight);
        Assert.Equal((20, 10, 4), (request.Length, request.Width, request.Height));

        var shipment = await CurrentShipmentAsync(order.Code);
        Assert.NotNull(shipment);
        Assert.Equal(order.Code, shipment.Ref);
        var shipmentJson = data.GetProperty("shipment");
        Assert.Equal(shipment.Id, shipmentJson.GetProperty("id").GetString());
        Assert.Equal(shipment.TrackingCode, shipmentJson.GetProperty("trackingCode").GetString());

        // Lịch sử và audit đều ghi mã vận đơn nội bộ và mã vận đơn GHN, vì bảng đơn không có cột vận đơn.
        var history = (await HistoryOfAsync(order.Code)).Single(row => row.To == OrderStatus.Shipping);
        Assert.Equal(OrderStatus.Confirmed, history.From);
        Assert.Equal(staffId, history.ActorId);
        Assert.Contains(shipment.Id, history.Reason);
        Assert.Contains(shipment.TrackingCode, history.Reason);

        var audit = Assert.Single(await AuditOfAsync(order.Code, CreateAuditAction));
        Assert.Equal(staffId, audit.ActorId);
        Assert.Equal(shipment.Id, audit.After!.RootElement.GetProperty("shipmentId").GetString());
        Assert.Equal(shipment.TrackingCode, audit.After.RootElement.GetProperty("trackingCode").GetString());

        var statusEvent = Assert.Single(
            _factory.DomainEvents.OfType<OrderStatusChangedEvent>(), e => e.Code == order.Code && e.Status == OrderStatus.Shipping);
        Assert.Equal(OrderStatus.Confirmed, statusEvent.PreviousStatus);
    }

    [Fact]
    // Đơn SePay đã trả tiền thì shipper không thu hộ nữa, nên `CodAmount` bằng 0 dù tổng tiền khác 0.
    public async Task CreateShipment_SepayOrder_SendsZeroCodAmount()
    {
        var order = await PlaceOrderAsync("sepay", status: OrderStatus.Confirmed, paymentStatus: PaymentStatus.Paid);
        var (_, session) = await StaffAsync("sales");

        var response = await CreateShipmentAsync(order.Code, session);

        await ReadDataAsync(response);
        var request = Assert.Single(_factory.Recorder.CreateOrderRequests, r => r.ClientOrderCode == order.Code);
        Assert.True(order.Total > 0);
        Assert.Equal(0, request.CodAmount);
        Assert.Equal(order.Subtotal, request.InsuranceValue);
    }

    [Fact]
    // Có `parcel` trong body thì dùng đúng kích thước đó thay vì kiện dựng từ dòng hàng.
    public async Task CreateShipment_WithCustomParcel_SendsGivenDimensions()
    {
        var order = await PlaceOrderAsync("cod", quantity: 2);
        var (_, session) = await StaffAsync("sales");

        var response = await CreateShipmentAsync(order.Code, session, Parcel(1234, 31, 22, 13));

        await ReadDataAsync(response);
        var request = Assert.Single(_factory.Recorder.CreateOrderRequests, r => r.ClientOrderCode == order.Code);
        Assert.Equal((1234, 31, 22, 13), (request.Weight, request.Length, request.Width, request.Height));
    }

    public static TheoryData<string> InvalidParcelBodies =>
        new()
        {
            """{"parcel":{"weightGrams":0,"lengthCm":10,"widthCm":10,"heightCm":10}}""",
            """{"parcel":{"weightGrams":-5,"lengthCm":10,"widthCm":10,"heightCm":10}}""",
            """{"parcel":{"weightGrams":500,"lengthCm":10.5,"widthCm":10,"heightCm":10}}""",
            """{"parcel":{"weightGrams":"500","lengthCm":10,"widthCm":10,"heightCm":10}}""",
            """{"parcel":{"weightGrams":500,"lengthCm":10,"widthCm":10}}""",
            """{"parcel":{"weightGrams":99999999999,"lengthCm":10,"widthCm":10,"heightCm":10}}""",
            """{"parcel":{"weightGrams":500,"lengthCm":10,"widthCm":10,"heightCm":10,"color":"red"}}""",
            """{"parcel":null}""",
            """{"parcel":"nặng"}""",
            """{"weight":500}""",
            """[]""",
        };

    [Theory]
    [MemberData(nameof(InvalidParcelBodies))]
    // Kích thước sai trả 400 trước khi gọi GHN. Đơn giữ `confirmed` và không ghi gì.
    public async Task CreateShipment_InvalidParcel_Returns400AndDoesNotCallGhn(string rawBody)
    {
        var order = await PlaceOrderAsync("cod");
        var (_, session) = await StaffAsync("sales");
        var callsBefore = _factory.Recorder.TotalCallCount;

        var response = await CreateShipmentAsync(order.Code, session, rawBody);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(callsBefore, _factory.Recorder.TotalCallCount);
        Assert.Equal(OrderStatus.Confirmed, await StatusOfAsync(order.Code));
        Assert.Empty(await AuditOfAsync(order.Code, CreateAuditAction));
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PendingConfirmation)]
    [InlineData(OrderStatus.Shipping)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Returned)]
    [InlineData(OrderStatus.Cancelled)]
    // Chỉ đơn `confirmed` mới tạo được vận đơn. Mọi trạng thái khác trả 409 và không gọi GHN.
    public async Task CreateShipment_FromAnyOtherStatus_Returns409AndDoesNotCallGhn(OrderStatus status)
    {
        var order = await PlaceOrderAsync("cod", status: status);
        var (_, session) = await StaffAsync("sales");
        var callsBefore = _factory.Recorder.TotalCallCount;
        var eventsBefore = StatusEventCount(order.Code);

        var response = await CreateShipmentAsync(order.Code, session);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        Assert.Equal(callsBefore, _factory.Recorder.TotalCallCount);
        Assert.Equal(status, await StatusOfAsync(order.Code));
        Assert.Empty(await AuditOfAsync(order.Code, CreateAuditAction));
        Assert.Equal(eventsBefore, StatusEventCount(order.Code));
    }

    [Fact]
    public async Task CreateShipment_UnknownCode_Returns404()
    {
        var (_, session) = await StaffAsync("sales");

        var response = await CreateShipmentAsync("PK00000000", session);

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "ORDER_NOT_FOUND");
    }

    [Fact]
    // GHN tạo đơn xong nhưng phản hồi mất: 503, đơn giữ `confirmed`. Bấm lại không tạo đơn GHN thứ hai và đơn sang `shipping`.
    public async Task CreateShipment_RetryAfterUnavailableAfterProcessing_CreatesOnlyOneShipment()
    {
        var order = await PlaceOrderAsync("cod");
        var (_, session) = await StaffAsync("sales");
        var createdBefore = _factory.GhnClient.CreatedOrderCount;
        _factory.GhnClient.SetFailure(FakeGhnOperation.CreateOrder, FakeGhnFailure.UnavailableAfterProcessing);

        var first = await CreateShipmentAsync(order.Code, session);

        await AssertErrorAsync(first, HttpStatusCode.ServiceUnavailable, "SHIPPING_UNAVAILABLE");
        Assert.Equal(OrderStatus.Confirmed, await StatusOfAsync(order.Code));
        Assert.Null(await CurrentShipmentAsync(order.Code));

        _factory.GhnClient.ClearFailures();
        var second = await CreateShipmentAsync(order.Code, session);

        var data = await ReadDataAsync(second);
        Assert.Equal("shipping", data.GetProperty("status").GetString());
        Assert.Equal(createdBefore + 1, _factory.GhnClient.CreatedOrderCount);
        Assert.Equal(OrderStatus.Shipping, await StatusOfAsync(order.Code));
        Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Shipping);
    }

    [Fact]
    // GHN từ chối: 422 `SHIPPING_REJECTED` mang thông báo của shipping. Đơn giữ `confirmed`, không có lịch sử hay audit mới.
    public async Task CreateShipment_Rejected_Returns422AndKeepsOrderConfirmed()
    {
        var order = await PlaceOrderAsync("cod");
        var (_, session) = await StaffAsync("sales");
        _factory.GhnClient.SetFailure(FakeGhnOperation.CreateOrder, FakeGhnFailure.Rejected);
        var historyBefore = (await HistoryOfAsync(order.Code)).Count;
        var eventsBefore = StatusEventCount(order.Code);

        var response = await CreateShipmentAsync(order.Code, session);

        var error = await AssertErrorAsync(response, (HttpStatusCode)422, "SHIPPING_REJECTED");
        Assert.Contains("Client giả được đặt chế độ từ chối.", error.GetProperty("message").GetString());
        Assert.Equal(OrderStatus.Confirmed, await StatusOfAsync(order.Code));
        Assert.Equal(historyBefore, (await HistoryOfAsync(order.Code)).Count);
        Assert.Empty(await AuditOfAsync(order.Code, CreateAuditAction));
        Assert.Equal(eventsBefore, StatusEventCount(order.Code));
    }

    [Fact]
    // Đơn bị hủy giữa lúc gọi GHN và lúc khóa dòng đơn: vận đơn vừa tạo phải bị hủy, và route trả 409.
    public async Task CreateShipment_OrderCancelledBetweenGhnAndLock_CancelsNewShipmentAndReturns409()
    {
        var order = await PlaceOrderAsync("cod");
        var (_, session) = await StaffAsync("sales");
        _factory.Recorder.AfterCreateOrder = () => SetOrderStateAsync(order.Code, OrderStatus.Cancelled);
        var cancelledBefore = _factory.Recorder.CancelledOrderCodes.Count;

        var response = await CreateShipmentAsync(order.Code, session);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        Assert.Equal(OrderStatus.Cancelled, await StatusOfAsync(order.Code));
        Assert.Equal(cancelledBefore + 1, _factory.Recorder.CancelledOrderCodes.Count);
        Assert.Null(await CurrentShipmentAsync(order.Code));
        Assert.Empty(await AuditOfAsync(order.Code, CreateAuditAction));
        Assert.DoesNotContain(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Shipping);
    }

    [Fact]
    // Bấm tạo hai lần gần như cùng lúc: lần hai chen vào khe sau lời gọi GHN của lần một. Chỉ có một vận đơn và một dòng lịch sử.
    public async Task CreateShipment_DoubleClickInsideGhnWindow_KeepsSingleShipmentAndHistory()
    {
        var order = await PlaceOrderAsync("cod");
        var (_, session) = await StaffAsync("sales");
        var createdBefore = _factory.GhnClient.CreatedOrderCount;
        var nestedResponse = default(HttpResponseMessage);
        _factory.Recorder.AfterCreateOrder = async () =>
        {
            // Chỉ chen một lần, vì lời gọi lồng nhau cũng đi qua hàm này.
            _factory.Recorder.AfterCreateOrder = null;
            nestedResponse = await CreateShipmentAsync(order.Code, session);
        };

        var outerResponse = await CreateShipmentAsync(order.Code, session);

        await ReadDataAsync(outerResponse);
        Assert.NotNull(nestedResponse);
        await ReadDataAsync(nestedResponse);
        Assert.Equal(createdBefore + 1, _factory.GhnClient.CreatedOrderCount);
        Assert.Equal(OrderStatus.Shipping, await StatusOfAsync(order.Code));
        Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Shipping);
        Assert.Single(await AuditOfAsync(order.Code, CreateAuditAction));
        var shipment = await CurrentShipmentAsync(order.Code);
        Assert.NotNull(shipment);
        Assert.DoesNotContain(shipment.TrackingCode, _factory.Recorder.CancelledOrderCodes);
    }

    [Fact]
    // SKU thiếu cân nặng hoặc kích thước mà body không có `parcel` thì không dựng được kiện. Trả 422 và không gọi GHN.
    public async Task CreateShipment_SkuMissingDimensionsWithoutParcel_Returns422()
    {
        var order = await PlaceOrderAsync("cod");
        await using (var catalog = _database.CreateCatalogContext())
        {
            await catalog.Skus.Where(sku => sku.Id == order.SkuId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(sku => sku.WeightGrams, (int?)null));
        }

        var (_, session) = await StaffAsync("sales");
        var callsBefore = _factory.Recorder.TotalCallCount;

        var withoutParcel = await CreateShipmentAsync(order.Code, session);

        await AssertErrorAsync(withoutParcel, (HttpStatusCode)422, "PARCEL_DIMENSIONS_MISSING");
        Assert.Equal(callsBefore, _factory.Recorder.TotalCallCount);

        // Có kiện nhập tay thì thao tác vẫn thành công.
        var withParcel = await CreateShipmentAsync(order.Code, session, Parcel(800, 20, 10, 5));
        Assert.Equal("shipping", (await ReadDataAsync(withParcel)).GetProperty("status").GetString());
    }

    // ---------- Hủy vận đơn ----------

    private async Task<(PlacedOrder Order, string StaffId, string Session, Shipment Shipment)> ShippingOrderAsync(
        string paymentMethod = "cod")
    {
        var order = await PlaceOrderAsync(paymentMethod);
        var (staffId, session) = await StaffAsync("sales");
        var created = await CreateShipmentAsync(order.Code, session);
        await ReadDataAsync(created);
        var shipment = (await CurrentShipmentAsync(order.Code))!;
        return (order, staffId, session, shipment);
    }

    [Fact]
    // Hủy vận đơn thành công thì đơn về `confirmed`, rồi nhân viên hủy đơn được bằng route của COM-15.
    public async Task CancelShipment_Succeeds_ReturnsOrderToConfirmedAndAllowsOrderCancellation()
    {
        var (order, staffId, session, shipment) = await ShippingOrderAsync();
        var eventsBefore = StatusEventCount(order.Code);

        var response = await CancelShipmentAsync(order.Code, session);

        var data = await ReadDataAsync(response);
        Assert.Equal("confirmed", data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("shipment").ValueKind);
        Assert.Null(await CurrentShipmentAsync(order.Code));
        Assert.Contains(shipment.TrackingCode, _factory.Recorder.CancelledOrderCodes);

        var history = (await HistoryOfAsync(order.Code)).Last(row => row.To == OrderStatus.Confirmed);
        Assert.Equal(OrderStatus.Shipping, history.From);
        Assert.Equal(staffId, history.ActorId);
        Assert.Contains(shipment.TrackingCode, history.Reason);
        Assert.Equal(eventsBefore + 1, StatusEventCount(order.Code));

        var audit = Assert.Single(await AuditOfAsync(order.Code, CancelAuditAction));
        Assert.Equal(staffId, audit.ActorId);
        Assert.Equal(shipment.Id, audit.Before!.RootElement.GetProperty("shipmentId").GetString());

        var cancelOrder = await SendAsync(
            HttpMethod.Post, $"{AdminOrdersPath}/{order.Code}/cancellation", session, new { reason = "Khách không nhận" });
        Assert.Equal("cancelled", (await ReadDataAsync(cancelOrder)).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.PendingConfirmation)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    // Đơn không ở `shipping` thì không có vận đơn để hủy. Trả 409 và không gọi GHN.
    public async Task CancelShipment_OrderNotShipping_Returns409(OrderStatus status)
    {
        var order = await PlaceOrderAsync("cod", status: status);
        var (_, session) = await StaffAsync("sales");
        var callsBefore = _factory.Recorder.TotalCallCount;

        var response = await CancelShipmentAsync(order.Code, session);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        Assert.Equal(callsBefore, _factory.Recorder.TotalCallCount);
        Assert.Equal(status, await StatusOfAsync(order.Code));
        Assert.Empty(await AuditOfAsync(order.Code, CancelAuditAction));
    }

    [Fact]
    // Đơn `shipping` nhưng không có vận đơn còn hiệu lực (dữ liệu lệch) thì trả 409.
    public async Task CancelShipment_ShippingOrderWithoutShipment_Returns409()
    {
        var order = await PlaceOrderAsync("cod", status: OrderStatus.Shipping);
        var (_, session) = await StaffAsync("sales");

        var response = await CancelShipmentAsync(order.Code, session);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        Assert.Equal(OrderStatus.Shipping, await StatusOfAsync(order.Code));
    }

    [Fact]
    // GHN từ chối hủy (shipper đã lấy hàng): 422, đơn giữ `shipping`, vận đơn còn nguyên, không có audit.
    public async Task CancelShipment_GhnRejects_KeepsOrderShipping()
    {
        var (order, _, session, shipment) = await ShippingOrderAsync();
        _factory.GhnClient.SetOrderStatus(shipment.TrackingCode, "picked");

        var response = await CancelShipmentAsync(order.Code, session);

        await AssertErrorAsync(response, (HttpStatusCode)422, "SHIPPING_REJECTED");
        Assert.Equal(OrderStatus.Shipping, await StatusOfAsync(order.Code));
        Assert.NotNull(await CurrentShipmentAsync(order.Code));
        Assert.Empty(await AuditOfAsync(order.Code, CancelAuditAction));
    }

    [Fact]
    // GHN lỗi mạng khi hủy: 503, đơn giữ `shipping`.
    public async Task CancelShipment_GhnUnavailable_Returns503AndKeepsOrderShipping()
    {
        var (order, _, session, _) = await ShippingOrderAsync();
        _factory.GhnClient.SetFailure(FakeGhnOperation.CancelOrder, FakeGhnFailure.Unavailable);

        var response = await CancelShipmentAsync(order.Code, session);

        await AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, "SHIPPING_UNAVAILABLE");
        Assert.Equal(OrderStatus.Shipping, await StatusOfAsync(order.Code));
        Assert.NotNull(await CurrentShipmentAsync(order.Code));
    }

    [Fact]
    // Sau khi hủy vận đơn thì tạo lại được vận đơn mới với mã GHN mới.
    public async Task CancelShipment_ThenCreateAgain_IssuesNewShipment()
    {
        var (order, _, session, first) = await ShippingOrderAsync();
        await ReadDataAsync(await CancelShipmentAsync(order.Code, session));

        var response = await CreateShipmentAsync(order.Code, session);

        var data = await ReadDataAsync(response);
        Assert.Equal("shipping", data.GetProperty("status").GetString());
        var second = (await CurrentShipmentAsync(order.Code))!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.TrackingCode, second.TrackingCode);
    }

    [Fact]
    // Luật bảo vệ điều kiện `shipping`: đơn đã giao vẫn còn vận đơn hiệu lực thì không hủy vận đơn ở GHN.
    public async Task CancelShipment_DeliveredOrderWithActiveShipment_Returns409AndDoesNotCallGhn()
    {
        var (order, _, session, shipment) = await ShippingOrderAsync();
        await SetOrderStateAsync(order.Code, OrderStatus.Delivered);
        var callsBefore = _factory.Recorder.TotalCallCount;

        var response = await CancelShipmentAsync(order.Code, session);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "INVALID_ORDER_TRANSITION");
        Assert.Equal(callsBefore, _factory.Recorder.TotalCallCount);
        Assert.Equal(OrderStatus.Delivered, await StatusOfAsync(order.Code));
        Assert.Equal(shipment.Id, (await CurrentShipmentAsync(order.Code))!.Id);
        Assert.Empty(await AuditOfAsync(order.Code, CancelAuditAction));
    }

    // ---------- GHN tự hủy vận đơn ----------

    [Fact]
    // GHN hủy vận đơn của đơn `shipping`: đơn về `confirmed` có cờ, đúng một dòng lịch sử `actorId` null và không có audit.
    public async Task CarrierCancelsShipment_MovesOrderToConfirmedWithFlagAndNoAudit()
    {
        var (order, _, _, shipment) = await ShippingOrderAsync();
        var eventsBefore = StatusEventCount(order.Code);

        await CarrierCancelsShipmentAsync(shipment);

        Assert.Equal(OrderStatus.Confirmed, await StatusOfAsync(order.Code));
        Assert.Equal([HandlerFlag], await FlagsOfAsync(order.Code));
        var history = Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Confirmed && row.From == OrderStatus.Shipping);
        Assert.Null(history.ActorId);
        Assert.Equal(eventsBefore + 1, StatusEventCount(order.Code));
        Assert.Empty(await AuditOfAsync(order.Code, CancelAuditAction));
        Assert.Null(await CurrentShipmentAsync(order.Code));
    }

    [Fact]
    // Nhân viên gọi `DELETE .../shipment` chạy luồng thật: đơn về `confirmed` không có cờ, một dòng lịch sử của nhân viên, một dòng audit.
    public async Task StaffCancelsShipment_DoesNotSetFlagAndWritesOneHistoryAndOneAudit()
    {
        var (order, staffId, session, _) = await ShippingOrderAsync();

        await ReadDataAsync(await CancelShipmentAsync(order.Code, session));

        Assert.Equal(OrderStatus.Confirmed, await StatusOfAsync(order.Code));
        Assert.Empty(await FlagsOfAsync(order.Code));
        var history = Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Confirmed && row.From == OrderStatus.Shipping);
        Assert.Equal(staffId, history.ActorId);
        var audit = Assert.Single(await AuditOfAsync(order.Code, CancelAuditAction));
        Assert.Equal(staffId, audit.ActorId);
    }

    [Fact]
    // Sự kiện hủy nguồn `Carrier` đến hai lần: một dòng lịch sử và cờ chỉ có một lần.
    public async Task CarrierCancelledEvent_ReceivedTwice_WritesHistoryOnceAndFlagOnce()
    {
        var (order, _, _, shipment) = await ShippingOrderAsync();
        var eventsBefore = StatusEventCount(order.Code);

        await CarrierCancelsShipmentAsync(shipment);
        await PublishShipmentEventAsync(shipment.Id, order.Code, ShipmentStatus.Cancelled);

        Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Confirmed && row.From == OrderStatus.Shipping);
        Assert.Equal([HandlerFlag], await FlagsOfAsync(order.Code));
        Assert.Equal(eventsBefore + 1, StatusEventCount(order.Code));
    }

    [Fact]
    // Sau khi GHN hủy, nhân viên tạo lại vận đơn: đơn sang `shipping` và mất cờ, vì nhân viên đã kiểm tra xong.
    public async Task CarrierCancelsShipment_ThenStaffCreatesAgain_ClearsFlag()
    {
        var (order, _, session, shipment) = await ShippingOrderAsync();
        await CarrierCancelsShipmentAsync(shipment);
        Assert.Equal([HandlerFlag], await FlagsOfAsync(order.Code));

        var response = await CreateShipmentAsync(order.Code, session);

        Assert.Equal("shipping", (await ReadDataAsync(response)).GetProperty("status").GetString());
        Assert.Empty(await FlagsOfAsync(order.Code));
    }

    [Fact]
    // Sự kiện hủy muộn của vận đơn cũ khi đơn đã có vận đơn hiệu lực mới bị bỏ qua. Đơn vẫn `shipping` và không có cờ.
    public async Task CarrierCancelledEvent_OfOldShipmentWhileNewShipmentActive_IsIgnored()
    {
        var (order, _, session, first) = await ShippingOrderAsync();
        await CarrierCancelsShipmentAsync(first);
        await ReadDataAsync(await CreateShipmentAsync(order.Code, session));
        var historyBefore = (await HistoryOfAsync(order.Code)).Count;

        await PublishShipmentEventAsync(first.Id, order.Code, ShipmentStatus.Cancelled);

        Assert.Equal(OrderStatus.Shipping, await StatusOfAsync(order.Code));
        Assert.Empty(await FlagsOfAsync(order.Code));
        Assert.Equal(historyBefore, (await HistoryOfAsync(order.Code)).Count);
    }

    [Fact]
    // Bộ lọc `flag=shipment_cancelled` của danh sách đơn chỉ trả đơn có cờ.
    public async Task CarrierCancelsShipment_FlagIsFilterableInOrderList()
    {
        var (flagged, _, session, shipment) = await ShippingOrderAsync();
        var (unflagged, _, _, _) = await ShippingOrderAsync();
        await CarrierCancelsShipmentAsync(shipment);

        var response = await SendAsync(HttpMethod.Get, $"{AdminOrdersPath}?flag={HandlerFlag}&limit=100", session);

        var data = await ReadDataAsync(response);
        var codes = data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToList();
        Assert.Contains(flagged.Code, codes);
        Assert.DoesNotContain(unflagged.Code, codes);
    }

    // ---------- Sự kiện trạng thái giao hàng ----------

    private async Task PublishShipmentEventAsync(
        string shipmentId,
        string reference,
        ShipmentStatus status,
        ShipmentStatusChangeSource source = ShipmentStatusChangeSource.Carrier)
    {
        var dispatcher = _factory.Services.GetRequiredService<IEventDispatcher>();
        await dispatcher.PublishAsync(
            new ShipmentStatusChangedEvent(shipmentId, reference, status, PreviousStatus: null, DateTimeOffset.UtcNow, source));
    }

    // Chạy job đồng bộ thật của shipping. Job tra GHN giả, áp trạng thái mới và phát sự kiện nguồn `Carrier` như webhook.
    private async Task RunCarrierSyncAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var shippingDatabase = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var updater = new ShipmentStatusUpdater(
            shippingDatabase,
            scope.ServiceProvider.GetRequiredService<IEventDispatcher>(),
            TimeProvider.System,
            NullLogger<ShipmentStatusUpdater>.Instance);
        var processor = new ShipmentStatusSyncProcessor(
            shippingDatabase,
            scope.ServiceProvider.GetRequiredService<IGhnClient>(),
            updater,
            NullLogger<ShipmentStatusSyncProcessor>.Instance);
        var result = await processor.SyncAsync(CancellationToken.None);
        Assert.Equal(0, result.Failed);
    }

    private async Task<List<string>> FlagsOfAsync(string code)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().Where(order => order.Code == code).Select(order => order.Flags).SingleAsync();
    }

    // Đưa đơn `shipping` (tạo bằng route thật) về trạng thái GHN đã hủy vận đơn rồi chạy job đồng bộ.
    private async Task CarrierCancelsShipmentAsync(Shipment shipment)
    {
        _factory.GhnClient.SetOrderStatus(shipment.TrackingCode, "cancel");
        await RunCarrierSyncAsync();
    }

    private async Task<(PlacedOrder Order, Shipment Shipment)> ShippingOrderWithShipmentAsync(
        string paymentMethod = "cod", PaymentStatus? paymentStatus = null)
    {
        var order = await PlaceOrderAsync(paymentMethod, status: OrderStatus.Shipping, paymentStatus: paymentStatus);
        var shipment = await CreateShipmentDirectAsync(order.Code);
        return (order, shipment);
    }

    [Fact]
    // `Delivered` đưa đơn sang `delivered`. Lịch sử có `actorId` null, không có audit và có một sự kiện đổi trạng thái.
    public async Task ShipmentDelivered_MovesOrderToDelivered()
    {
        var (order, shipment) = await ShippingOrderWithShipmentAsync();
        var eventsBefore = StatusEventCount(order.Code);

        await PublishShipmentEventAsync(shipment.Id, order.Code, ShipmentStatus.Delivered);

        Assert.Equal(OrderStatus.Delivered, await StatusOfAsync(order.Code));
        var history = (await HistoryOfAsync(order.Code)).Single(row => row.To == OrderStatus.Delivered);
        Assert.Equal(OrderStatus.Shipping, history.From);
        Assert.Null(history.ActorId);
        Assert.Equal(eventsBefore + 1, StatusEventCount(order.Code));
        Assert.Empty(await AuditOfAsync(order.Code, CreateAuditAction));
    }

    [Fact]
    // `Returned` đưa đơn sang `returned` và hàng không tự nhập lại kho. Kho giữ nguyên số sau khi đặt đơn.
    public async Task ShipmentReturned_MovesOrderToReturnedWithoutRestockingInventory()
    {
        var (order, shipment) = await ShippingOrderWithShipmentAsync();
        var stockBefore = await _kit.StockOfAsync(order.SkuId);
        var movementsBefore = await _kit.MovementCountOfAsync(order.SkuId);

        await PublishShipmentEventAsync(shipment.Id, order.Code, ShipmentStatus.Returned);

        Assert.Equal(OrderStatus.Returned, await StatusOfAsync(order.Code));
        Assert.Equal(stockBefore, await _kit.StockOfAsync(order.SkuId));
        Assert.Equal(movementsBefore, await _kit.MovementCountOfAsync(order.SkuId));
    }

    [Theory]
    [InlineData(ShipmentStatus.PendingPickup)]
    [InlineData(ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.Delivering)]
    [InlineData(ShipmentStatus.DeliveryFailed)]
    [InlineData(ShipmentStatus.Returning)]
    [InlineData(ShipmentStatus.Cancelled)]
    [InlineData(ShipmentStatus.Exception)]
    // Trạng thái giao khác không đổi đơn và không ghi lịch sử.
    public async Task ShipmentOtherStatus_DoesNotChangeOrder(ShipmentStatus status)
    {
        var (order, shipment) = await ShippingOrderWithShipmentAsync();
        var historyBefore = (await HistoryOfAsync(order.Code)).Count;

        await PublishShipmentEventAsync(shipment.Id, order.Code, status);

        Assert.Equal(OrderStatus.Shipping, await StatusOfAsync(order.Code));
        Assert.Equal(historyBefore, (await HistoryOfAsync(order.Code)).Count);
    }

    [Fact]
    // Sự kiện mang `ShipmentId` không phải vận đơn hiện tại của đơn bị bỏ qua, ví dụ sự kiện muộn của vận đơn đã hủy.
    public async Task ShipmentEvent_WithDifferentShipmentId_IsIgnored()
    {
        var (order, _) = await ShippingOrderWithShipmentAsync();
        var historyBefore = (await HistoryOfAsync(order.Code)).Count;
        var eventsBefore = StatusEventCount(order.Code);

        await PublishShipmentEventAsync("shipment-cu-da-huy", order.Code, ShipmentStatus.Delivered);

        Assert.Equal(OrderStatus.Shipping, await StatusOfAsync(order.Code));
        Assert.Equal(historyBefore, (await HistoryOfAsync(order.Code)).Count);
        Assert.Equal(eventsBefore, StatusEventCount(order.Code));
    }

    [Fact]
    // Đơn không có vận đơn còn hiệu lực thì sự kiện bị bỏ qua, và không ném lỗi.
    public async Task ShipmentEvent_ForOrderWithoutCurrentShipment_IsIgnored()
    {
        var order = await PlaceOrderAsync("cod", status: OrderStatus.Shipping);

        await PublishShipmentEventAsync("bat-ky", order.Code, ShipmentStatus.Delivered);

        Assert.Equal(OrderStatus.Shipping, await StatusOfAsync(order.Code));
    }

    [Fact]
    // `Ref` không khớp đơn nào (vận đơn do kênh khác tạo) thì sự kiện bị bỏ qua và không ném lỗi.
    public async Task ShipmentEvent_WithUnknownRef_IsIgnored()
    {
        await PublishShipmentEventAsync("bat-ky", "PK99999999", ShipmentStatus.Delivered);
    }

    [Fact]
    // Nhận cùng sự kiện hai lần: chỉ một dòng lịch sử và một sự kiện đổi trạng thái.
    public async Task ShipmentDelivered_ReceivedTwice_WritesHistoryAndEventOnce()
    {
        var (order, shipment) = await ShippingOrderWithShipmentAsync();
        var eventsBefore = StatusEventCount(order.Code);

        await PublishShipmentEventAsync(shipment.Id, order.Code, ShipmentStatus.Delivered);
        await PublishShipmentEventAsync(shipment.Id, order.Code, ShipmentStatus.Delivered);

        Assert.Single(await HistoryOfAsync(order.Code), row => row.To == OrderStatus.Delivered);
        Assert.Equal(eventsBefore + 1, StatusEventCount(order.Code));

        // Nhận lại cùng sự kiện là bình thường, nên không được ghi cảnh báo như một chuyển trạng thái bị từ chối.
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            log => log.Level >= LogLevel.Warning && log.Message.Contains(order.Code));
    }

    [Fact]
    // Đơn COD hoàn về khi chưa trả tiền thì trạng thái thanh toán giữ nguyên, vì không có tiền để hoàn.
    public async Task ShipmentReturned_UnpaidCodOrder_KeepsPaymentUnpaid()
    {
        var (order, shipment) = await ShippingOrderWithShipmentAsync("cod", PaymentStatus.Unpaid);

        await PublishShipmentEventAsync(shipment.Id, order.Code, ShipmentStatus.Returned);

        Assert.Equal(OrderStatus.Returned, await StatusOfAsync(order.Code));
        Assert.Equal(PaymentStatus.Unpaid, await PaymentStatusOfAsync(order.Code));
    }

    [Fact]
    // Đơn SePay đã trả tiền mà hoàn về thì thanh toán chuyển sang chờ hoàn tiền.
    public async Task ShipmentReturned_PaidSepayOrder_SetsPaymentToRefundPending()
    {
        var (order, shipment) = await ShippingOrderWithShipmentAsync("sepay", PaymentStatus.Paid);

        await PublishShipmentEventAsync(shipment.Id, order.Code, ShipmentStatus.Returned);

        Assert.Equal(OrderStatus.Returned, await StatusOfAsync(order.Code));
        Assert.Equal(PaymentStatus.RefundPending, await PaymentStatusOfAsync(order.Code));
    }

    [Fact]
    // Đơn đã giao mà SePay đã trả tiền thì giữ `paid`.
    public async Task ShipmentDelivered_PaidSepayOrder_KeepsPaymentPaid()
    {
        var (order, shipment) = await ShippingOrderWithShipmentAsync("sepay", PaymentStatus.Paid);

        await PublishShipmentEventAsync(shipment.Id, order.Code, ShipmentStatus.Delivered);

        Assert.Equal(OrderStatus.Delivered, await StatusOfAsync(order.Code));
        Assert.Equal(PaymentStatus.Paid, await PaymentStatusOfAsync(order.Code));
    }

    [Fact]
    // Đơn đã ở trạng thái khác `shipping` (ví dụ đã hủy) thì sự kiện không kéo đơn sang `delivered`.
    public async Task ShipmentDelivered_OrderNotShipping_IsIgnored()
    {
        var order = await PlaceOrderAsync("cod", status: OrderStatus.Confirmed);
        var shipment = await CreateShipmentDirectAsync(order.Code);

        await PublishShipmentEventAsync(shipment.Id, order.Code, ShipmentStatus.Delivered);

        Assert.Equal(OrderStatus.Confirmed, await StatusOfAsync(order.Code));
    }

    // ---------- Gợi ý kiện trong chi tiết đơn ----------

    private async Task<JsonElement> DetailAsync(string code, string session) =>
        await ReadDataAsync(await SendAsync(HttpMethod.Get, $"{AdminOrdersPath}/{code}", session));

    [Fact]
    // Đơn `confirmed` có `suggestedParcel` dựng từ dòng hàng, để form tạo vận đơn điền sẵn.
    public async Task Detail_ConfirmedOrder_ReturnsSuggestedParcel()
    {
        var order = await PlaceOrderAsync("cod", quantity: 3, status: OrderStatus.Confirmed);
        var (_, session) = await StaffAsync("sales");

        var data = await DetailAsync(order.Code, session);

        var parcel = data.GetProperty("suggestedParcel");
        Assert.Equal(SeedUnitWeightGrams * 3, parcel.GetProperty("weightGrams").GetInt32());
        Assert.Equal(20, parcel.GetProperty("lengthCm").GetInt32());
        Assert.Equal(10, parcel.GetProperty("widthCm").GetInt32());
        Assert.Equal(6, parcel.GetProperty("heightCm").GetInt32());
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PendingConfirmation)]
    [InlineData(OrderStatus.Shipping)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Returned)]
    [InlineData(OrderStatus.Cancelled)]
    // Chỉ đơn `confirmed` mới cần gợi ý kiện. Trạng thái khác trả `null`.
    public async Task Detail_NotConfirmedOrder_ReturnsNullSuggestedParcel(OrderStatus status)
    {
        var order = await PlaceOrderAsync("cod", status: status);
        var (_, session) = await StaffAsync("sales");

        var data = await DetailAsync(order.Code, session);

        Assert.Equal(JsonValueKind.Null, data.GetProperty("suggestedParcel").ValueKind);
    }

    [Fact]
    // SKU thiếu số đo thì không gợi ý được. Trả `null` thay vì đoán, và chi tiết đơn vẫn đọc được.
    public async Task Detail_ConfirmedOrderWithMissingDimensions_ReturnsNullSuggestedParcel()
    {
        var order = await PlaceOrderAsync("cod", status: OrderStatus.Confirmed);
        await using (var catalog = _database.CreateCatalogContext())
        {
            await catalog.Skus.Where(sku => sku.Id == order.SkuId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(sku => sku.LengthCm, (int?)null));
        }

        var (_, session) = await StaffAsync("sales");

        var data = await DetailAsync(order.Code, session);

        Assert.Equal(JsonValueKind.Null, data.GetProperty("suggestedParcel").ValueKind);
    }

    // ---------- Quyền ----------

    [Fact]
    // `support` không có `orders:fulfill`: 403 cho cả hai route, và khách vãng lai nhận 401. Đơn và GHN không đổi.
    public async Task ShipmentRoutes_WithoutPermission_AreRejectedAndNothingChanges()
    {
        var order = await PlaceOrderAsync("cod");
        var (_, supportSession) = await StaffAsync("support");
        var callsBefore = _factory.Recorder.TotalCallCount;

        await AssertErrorAsync(await CreateShipmentAsync(order.Code, supportSession), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertErrorAsync(await CancelShipmentAsync(order.Code, supportSession), HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateShipmentAsync(order.Code, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CancelShipmentAsync(order.Code, null)).StatusCode);

        Assert.Equal(callsBefore, _factory.Recorder.TotalCallCount);
        Assert.Equal(OrderStatus.Confirmed, await StatusOfAsync(order.Code));
        Assert.Empty(await AuditOfAsync(order.Code, CreateAuditAction));
    }
}
