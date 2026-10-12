using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-10: Theo dõi đơn và khách tự hủy.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class OrderTrackingTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string OrdersPath = "/api/orders";
    private const string WebOrigin = "http://localhost:5173";

    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];

    public OrderTrackingTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
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
    }

    public async Task DisposeAsync()
    {
        await DeleteOrdersOfThisTestAsync();
        await _kit.CleanupAsync();
    }

    private string NewKey()
    {
        var key = "key-" + Guid.NewGuid().ToString("N");
        _keys.Add(key);
        return key;
    }

    private async Task<(string Code, string Token, string SkuId)> CreateOrderAsync(
        string anonymousId,
        int price = 100_000,
        int quantity = 2,
        int stock = 10,
        string paymentMethod = "cod",
        string? sessionToken = null)
    {
        // Thêm sản phẩm vào giỏ trước khi gọi đặt đơn.
        var product = await _kit.AddProductAsync(price: price, stock: stock);
        var skuId = product.SkuIds[0];
        var addResponse = await _kit.AddAsync(anonymousId, skuId, quantity, sessionToken: sessionToken);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        // Lấy tổng tiền preview để đặt đơn thành công.
        var previewBody = new
        {
            address = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            paymentMethod,
        };
        var previewResponse = await _kit.SendAsync(HttpMethod.Post, "/api/checkout/previews", anonymousId, previewBody, sessionToken);
        var previewData = await CartTestKit.ReadCartAsync(previewResponse);
        var total = previewData.GetProperty("total").GetInt32();

        // Gửi request tạo đơn bằng route chuẩn.
        var orderBody = new Dictionary<string, object?>
        {
            ["contact"] = new { name = "Nguyễn Văn An", phone = "0901234567", email = "an@example.com" },
            ["address"] = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            ["paymentMethod"] = paymentMethod,
            ["expectedTotal"] = total,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, OrdersPath);
        var cookie = $"pk_anon={anonymousId}";
        if (sessionToken is not null)
        {
            cookie += $"; pk_session={sessionToken}";
        }

        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", WebOrigin);
        request.Headers.Add("Idempotency-Key", NewKey());
        request.Content = new StringContent(JsonSerializer.Serialize(orderBody), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        var data = json.GetProperty("data");
        var code = data.GetProperty("code").GetString()!;
        var token = data.GetProperty("accessToken").GetString()!;
        return (code, token, skuId);
    }

    private Task<HttpResponseMessage> SendGetAsync(
        string path,
        string anonymousId,
        string? sessionToken = null,
        string? tokenQuery = null)
    {
        // Tạo chuỗi truy vấn khi có token.
        var uri = string.IsNullOrEmpty(tokenQuery) ? path : $"{path}?t={tokenQuery}";
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var cookie = $"pk_anon={anonymousId}";
        if (sessionToken is not null)
        {
            cookie += $"; pk_session={sessionToken}";
        }

        request.Headers.Add("Cookie", cookie);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendCancellationAsync(
        string code,
        string anonymousId,
        string? sessionToken = null,
        string? tokenQuery = null,
        object? body = null)
    {
        // Gửi yêu cầu tự hủy qua phương thức POST.
        var uri = string.IsNullOrEmpty(tokenQuery)
            ? $"{OrdersPath}/{code}/cancellation"
            : $"{OrdersPath}/{code}/cancellation?t={tokenQuery}";
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        var cookie = $"pk_anon={anonymousId}";
        if (sessionToken is not null)
        {
            cookie += $"; pk_session={sessionToken}";
        }

        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", WebOrigin);
        var content = body is null ? "{}" : JsonSerializer.Serialize(body);
        request.Content = new StringContent(content, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        return _client.SendAsync(request);
    }

    private async Task DeleteOrdersOfThisTestAsync()
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        var orderIds = await commerce.Orders
            .Where(order => _keys.Contains(order.IdempotencyKey))
            .Select(order => order.Id)
            .ToListAsync();
        await commerce.OrderStatusHistory.Where(history => orderIds.Contains(history.OrderId)).ExecuteDeleteAsync();
        await commerce.OrderItems.Where(item => orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
        await commerce.Orders.Where(order => orderIds.Contains(order.Id)).ExecuteDeleteAsync();
    }

    // Chủ tài khoản xem được đơn của mình mà không cần token.
    [Fact]
    public async Task GetOrder_WhenOwnerAccessesOwnOrder_Returns200WithoutToken()
    {
        var customer = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var (code, _, _) = await CreateOrderAsync(anonymousId, sessionToken: customer.SessionToken);

        // Chủ tài khoản gọi xem đơn không kèm query token.
        var response = await SendGetAsync($"{OrdersPath}/{code}", anonymousId, sessionToken: customer.SessionToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await CartTestKit.ReadJsonAsync(response);
        var data = json.GetProperty("data");
        Assert.Equal(code, data.GetProperty("code").GetString());
        Assert.Equal("pending_confirmation", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("canCancel").GetBoolean());
        Assert.Null(data.GetProperty("trackingCode").GetString());
    }

    // Khách vãng lai có token hợp lệ xem được đơn. Cùng cookie nhưng không có token thì trả 404.
    [Fact]
    public async Task GetOrder_WhenGuestHasValidToken_Returns200_WithoutToken_Returns404()
    {
        var anonymousId = _kit.NewAnonymousId();
        var (code, token, _) = await CreateOrderAsync(anonymousId);

        // Khách vãng lai gửi token hợp lệ xem được chi tiết đơn.
        var validResponse = await SendGetAsync($"{OrdersPath}/{code}", anonymousId, tokenQuery: token);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

        // Cùng cookie ẩn danh nhưng không có token trong query thì bị từ chối với 404.
        var missingTokenResponse = await SendGetAsync($"{OrdersPath}/{code}", anonymousId);
        Assert.Equal(HttpStatusCode.NotFound, missingTokenResponse.StatusCode);
        var missingJson = await CartTestKit.ReadJsonAsync(missingTokenResponse);
        Assert.Equal("ORDER_NOT_FOUND", missingJson.GetProperty("error").GetProperty("code").GetString());
    }

    // Kiểm tra từng trường hợp 404 theo quy định. Thân lỗi của mã không tồn tại và token sai phải giống nhau.
    [Fact]
    public async Task GetOrder_404Cases_ReturnSameErrorPayload()
    {
        var customer = await _kit.CreateCustomerAsync();
        var otherCustomer = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var (code, token, _) = await CreateOrderAsync(anonymousId, sessionToken: customer.SessionToken);

        // Trường hợp 1: Mã đơn không tồn tại.
        var notFoundResponse = await SendGetAsync($"{OrdersPath}/PK99999999", anonymousId);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        var notFoundBody = await notFoundResponse.Content.ReadAsStringAsync();

        // Trường hợp 2: Khách vãng lai không có token.
        var noTokenResponse = await SendGetAsync($"{OrdersPath}/{code}", anonymousId);
        Assert.Equal(HttpStatusCode.NotFound, noTokenResponse.StatusCode);

        // Trường hợp 3: Token sai. Thân lỗi phải giống hệt trường hợp mã không tồn tại.
        var wrongTokenResponse = await SendGetAsync($"{OrdersPath}/{code}", anonymousId, tokenQuery: "wrong-token-value");
        Assert.Equal(HttpStatusCode.NotFound, wrongTokenResponse.StatusCode);
        var wrongTokenBody = await wrongTokenResponse.Content.ReadAsStringAsync();
        Assert.Equal(notFoundBody, wrongTokenBody);

        // Trường hợp 4: Token rỗng.
        var emptyTokenResponse = await SendGetAsync($"{OrdersPath}/{code}", anonymousId, tokenQuery: "");
        Assert.Equal(HttpStatusCode.NotFound, emptyTokenResponse.StatusCode);

        // Trường hợp 5: Đăng nhập bằng tài khoản khác mà không có token.
        var otherUserResponse = await SendGetAsync($"{OrdersPath}/{code}", anonymousId, sessionToken: otherCustomer.SessionToken);
        Assert.Equal(HttpStatusCode.NotFound, otherUserResponse.StatusCode);
    }

    // Token của đơn này dùng cho đơn khác bị từ chối với lỗi 404.
    [Fact]
    public async Task GetOrder_WhenUsingTokenOfDifferentOrder_Returns404()
    {
        var anonymousId = _kit.NewAnonymousId();
        var (codeA, tokenA, _) = await CreateOrderAsync(anonymousId);
        var (codeB, _, _) = await CreateOrderAsync(anonymousId);

        // Dùng token của đơn A để truy cập đơn B phải nhận lỗi 404.
        var response = await SendGetAsync($"{OrdersPath}/{codeB}", anonymousId, tokenQuery: tokenA);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Danh sách đơn chỉ hiện đơn của tài khoản gọi. Người chưa đăng nhập nhận lỗi 401.
    [Fact]
    public async Task GetOrders_WhenUnauthenticated_Returns401_WhenAuthenticated_ReturnsOwnOrders()
    {
        var anonymousId = _kit.NewAnonymousId();

        // Chưa đăng nhập gọi danh sách đơn phải nhận 401.
        var unauthResponse = await SendGetAsync(OrdersPath, anonymousId);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        var customer = await _kit.CreateCustomerAsync();
        var (code1, _, _) = await CreateOrderAsync(anonymousId, sessionToken: customer.SessionToken);
        var (code2, _, _) = await CreateOrderAsync(anonymousId, sessionToken: customer.SessionToken);

        // Khách khác tạo đơn để xác minh không bị lộ đơn của nhau.
        var otherCustomer = await _kit.CreateCustomerAsync();
        var (otherCode, _, _) = await CreateOrderAsync(anonymousId, sessionToken: otherCustomer.SessionToken);

        // Đọc danh sách đơn phân trang với limit là 10.
        var response = await SendGetAsync($"{OrdersPath}?page=1&limit=10", anonymousId, sessionToken: customer.SessionToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        var data = json.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().ToList();

        // Chỉ chứa đúng hai đơn của khách hiện tại, đơn mới nhất đứng đầu.
        Assert.Equal(2, items.Count);
        Assert.Equal(code2, items[0].GetProperty("code").GetString());
        Assert.Equal(code1, items[1].GetProperty("code").GetString());
        Assert.DoesNotContain(items, item => item.GetProperty("code").GetString() == otherCode);

        // Kiểm tra cấu trúc trường không lộ thông tin cá nhân.
        var first = items[0];
        Assert.True(first.TryGetProperty("itemCount", out _));
        Assert.True(first.TryGetProperty("firstItemName", out _));
        Assert.False(first.TryGetProperty("contact", out _));
        Assert.False(first.TryGetProperty("shippingAddress", out _));
        Assert.False(first.TryGetProperty("invoice", out _));
    }

    // Route payment trả đúng tập trường và không lộ thông tin cá nhân.
    [Fact]
    public async Task GetPayment_ReturnsExactFieldSet_WithoutPersonalInformation()
    {
        var anonymousId = _kit.NewAnonymousId();
        var (code, token, _) = await CreateOrderAsync(anonymousId);

        var response = await SendGetAsync($"{OrdersPath}/{code}/payment", anonymousId, tokenQuery: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        var data = json.GetProperty("data");

        // Tập trường của data phải khớp chính xác 6 trường nghiệp vụ theo quyết định 4.
        var properties = data.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToList();
        var expectedProperties = new[]
        {
            "code",
            "paymentDeadline",
            "paymentMethod",
            "paymentStatus",
            "status",
            "total",
        }.OrderBy(name => name).ToList();

        Assert.Equal(expectedProperties, properties);
    }
    // Route payment trả 404 cho mọi trường hợp thiếu token hoặc token không hợp lệ.
    [Fact]
    public async Task GetPayment_404Cases_WhenUnauthorizedOrInvalidToken_Returns404()
    {
        var customer = await _kit.CreateCustomerAsync();
        var otherCustomer = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var (code, _, _) = await CreateOrderAsync(anonymousId, sessionToken: customer.SessionToken);
        var (_, otherToken, _) = await CreateOrderAsync(anonymousId);

        // Trường hợp 1: Không đăng nhập và không có token.
        var noTokenResponse = await SendGetAsync($"{OrdersPath}/{code}/payment", anonymousId);
        Assert.Equal(HttpStatusCode.NotFound, noTokenResponse.StatusCode);
        var noTokenJson = await CartTestKit.ReadJsonAsync(noTokenResponse);
        Assert.Equal("ORDER_NOT_FOUND", noTokenJson.GetProperty("error").GetProperty("code").GetString());

        // Trường hợp 2: Token sai.
        var wrongTokenResponse = await SendGetAsync($"{OrdersPath}/{code}/payment", anonymousId, tokenQuery: "wrong-token");
        Assert.Equal(HttpStatusCode.NotFound, wrongTokenResponse.StatusCode);
        var wrongTokenJson = await CartTestKit.ReadJsonAsync(wrongTokenResponse);
        Assert.Equal("ORDER_NOT_FOUND", wrongTokenJson.GetProperty("error").GetProperty("code").GetString());

        // Trường hợp 3: Token của đơn khác.
        var otherTokenResponse = await SendGetAsync($"{OrdersPath}/{code}/payment", anonymousId, tokenQuery: otherToken);
        Assert.Equal(HttpStatusCode.NotFound, otherTokenResponse.StatusCode);
        var otherTokenJson = await CartTestKit.ReadJsonAsync(otherTokenResponse);
        Assert.Equal("ORDER_NOT_FOUND", otherTokenJson.GetProperty("error").GetProperty("code").GetString());

        // Trường hợp 4: Đăng nhập tài khoản khác mà không có token.
        var otherUserResponse = await SendGetAsync($"{OrdersPath}/{code}/payment", anonymousId, sessionToken: otherCustomer.SessionToken);
        Assert.Equal(HttpStatusCode.NotFound, otherUserResponse.StatusCode);
        var otherUserJson = await CartTestKit.ReadJsonAsync(otherUserResponse);
        Assert.Equal("ORDER_NOT_FOUND", otherUserJson.GetProperty("error").GetProperty("code").GetString());
    }

    // Route hủy trả 404 và không thay đổi trạng thái hoặc tồn kho khi không đủ quyền.
    [Fact]
    public async Task Cancel_404Cases_WhenUnauthorizedOrInvalidToken_DoesNotCancelOrReleaseStock()
    {
        var customer = await _kit.CreateCustomerAsync();
        var otherCustomer = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var (code, _, skuId) = await CreateOrderAsync(anonymousId, quantity: 2, stock: 10, paymentMethod: "sepay", sessionToken: customer.SessionToken);
        var (_, otherToken, _) = await CreateOrderAsync(anonymousId);

        var eventsBefore = _factory.DomainEvents.OfType<OrderStatusChangedEvent>().Count(e => e.Code == code);

        // Trường hợp 1: Không đăng nhập và không có token.
        var r1 = await SendCancellationAsync(code, anonymousId);
        Assert.Equal(HttpStatusCode.NotFound, r1.StatusCode);

        // Trường hợp 2: Token sai.
        var r2 = await SendCancellationAsync(code, anonymousId, tokenQuery: "wrong-token");
        Assert.Equal(HttpStatusCode.NotFound, r2.StatusCode);

        // Trường hợp 3: Token của đơn khác.
        var r3 = await SendCancellationAsync(code, anonymousId, tokenQuery: otherToken);
        Assert.Equal(HttpStatusCode.NotFound, r3.StatusCode);

        // Trường hợp 4: Đăng nhập tài khoản khác mà không có token.
        var r4 = await SendCancellationAsync(code, anonymousId, sessionToken: otherCustomer.SessionToken);
        Assert.Equal(HttpStatusCode.NotFound, r4.StatusCode);

        // Kiểm tra đơn vẫn giữ nguyên trạng thái ban đầu trong cơ sở dữ liệu.
        await using var commerce = _kit.Database.CreateCommerceContext();
        var order = await commerce.Orders.AsNoTracking().SingleAsync(o => o.Code == code);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);

        // Tồn kho không bị hoàn trả và vẫn giữ nguyên mức 8.
        Assert.Equal(8, await _kit.StockOfAsync(skuId));

        // Không phát thêm bất kỳ sự kiện thay đổi trạng thái nào cho đơn này.
        var eventsAfter = _factory.DomainEvents.OfType<OrderStatusChangedEvent>().Count(e => e.Code == code);
        Assert.Equal(eventsBefore, eventsAfter);
    }

    // Chủ tài khoản tự hủy đơn của chính mình mà không cần token truy cập.
    [Fact]
    public async Task Cancel_WhenOwnerCancelsOwnOrder_SucceedsWithoutTokenAndRecordsUserIdInHistory()
    {
        var customer = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var (code, _, skuId) = await CreateOrderAsync(anonymousId, quantity: 2, stock: 10, paymentMethod: "sepay", sessionToken: customer.SessionToken);

        // Chủ tài khoản gọi hủy đơn chỉ kèm cookie phiên mà không truyền token.
        var response = await SendCancellationAsync(code, anonymousId, sessionToken: customer.SessionToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        Assert.Equal("cancelled", json.GetProperty("data").GetProperty("status").GetString());

        // Tồn kho được hoàn trả thành công về mức 10.
        Assert.Equal(10, await _kit.StockOfAsync(skuId));

        // Dòng lịch sử trạng thái ghi nhận đúng UserId của người gọi.
        await using var commerce = _kit.Database.CreateCommerceContext();
        var order = await commerce.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Code == code);
        var cancelHistory = Assert.Single(order.StatusHistory, h => h.ToStatus == OrderStatus.Cancelled);
        Assert.Equal(customer.UserId, cancelHistory.ActorId);
        Assert.Equal("Khách tự hủy.", cancelHistory.Reason);

        // Sự kiện đổi trạng thái đơn được phát với trạng thái mới là Cancelled.
        Assert.Single(_factory.DomainEvents.OfType<OrderStatusChangedEvent>(), e => e.Code == code && e.Status == OrderStatus.Cancelled);
    }

    // Thân lỗi 404 của route payment và route hủy giống hệt thân lỗi mã không tồn tại.
    [Fact]
    public async Task GetPaymentAndCancel_404Payload_MatchesNonExistentOrderPayload()
    {
        var anonymousId = _kit.NewAnonymousId();
        var (code, _, _) = await CreateOrderAsync(anonymousId);

        // Lấy thân lỗi của mã đơn không tồn tại làm chuẩn so sánh.
        var notFoundPayment = await SendGetAsync($"{OrdersPath}/PK99999999/payment", anonymousId);
        Assert.Equal(HttpStatusCode.NotFound, notFoundPayment.StatusCode);
        var notFoundPaymentBody = await notFoundPayment.Content.ReadAsStringAsync();

        var notFoundCancel = await SendCancellationAsync("PK99999999", anonymousId);
        Assert.Equal(HttpStatusCode.NotFound, notFoundCancel.StatusCode);
        var notFoundCancelBody = await notFoundCancel.Content.ReadAsStringAsync();

        // So sánh phản hồi khi truyền token sai trên route payment.
        var wrongTokenPayment = await SendGetAsync($"{OrdersPath}/{code}/payment", anonymousId, tokenQuery: "wrong-token");
        Assert.Equal(HttpStatusCode.NotFound, wrongTokenPayment.StatusCode);
        var wrongPaymentBody = await wrongTokenPayment.Content.ReadAsStringAsync();
        Assert.Equal(notFoundPaymentBody, wrongPaymentBody);

        // So sánh phản hồi khi truyền token sai trên route hủy đơn.
        var wrongTokenCancel = await SendCancellationAsync(code, anonymousId, tokenQuery: "wrong-token");
        Assert.Equal(HttpStatusCode.NotFound, wrongTokenCancel.StatusCode);
        var wrongCancelBody = await wrongTokenCancel.Content.ReadAsStringAsync();
        Assert.Equal(notFoundCancelBody, wrongCancelBody);
    }

    // Tự hủy ở trạng thái chờ thanh toán thành công và hoàn trả tồn kho.
    [Fact]
    public async Task Cancel_WhenPendingPayment_SucceedsAndReleasesStockAndEmitsEvent()
    {
        var anonymousId = _kit.NewAnonymousId();
        var (code, token, skuId) = await CreateOrderAsync(anonymousId, quantity: 2, stock: 10, paymentMethod: "sepay");

        // Tồn kho sau khi đặt đơn giảm từ 10 xuống 8.
        Assert.Equal(8, await _kit.StockOfAsync(skuId));

        // Khách tự hủy đơn ở trạng thái chờ thanh toán.
        var response = await SendCancellationAsync(code, anonymousId, tokenQuery: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        var data = json.GetProperty("data");
        Assert.Equal("cancelled", data.GetProperty("status").GetString());

        // Tồn kho được hoàn trả lại thành 10 trong catalog.
        Assert.Equal(10, await _kit.StockOfAsync(skuId));

        // Kiểm tra bản ghi lịch sử trạng thái trong cơ sở dữ liệu.
        await using var commerce = _kit.Database.CreateCommerceContext();
        var order = await commerce.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Code == code);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        var cancellationHistory = Assert.Single(order.StatusHistory, h => h.ToStatus == OrderStatus.Cancelled);
        Assert.Equal("Khách tự hủy.", cancellationHistory.Reason);

        // Sự kiện đổi trạng thái đơn được phát qua dispatcher sau khi commit.
        var statusEvent = Assert.Single(
            _factory.DomainEvents.OfType<OrderStatusChangedEvent>(),
            e => e.Code == code && e.Status == OrderStatus.Cancelled);
        Assert.Equal(OrderStatus.PendingPayment, statusEvent.PreviousStatus);
    }

    // Tự hủy ở trạng thái chờ xác nhận thành công và hoàn trả tồn kho.
    [Fact]
    public async Task Cancel_WhenPendingConfirmation_SucceedsAndReleasesStockAndEmitsEvent()
    {
        var anonymousId = _kit.NewAnonymousId();
        var (code, token, skuId) = await CreateOrderAsync(anonymousId, quantity: 2, stock: 10, paymentMethod: "cod");

        // Tồn kho sau khi đặt đơn giảm từ 10 xuống 8.
        Assert.Equal(8, await _kit.StockOfAsync(skuId));

        // Khách tự hủy đơn ở trạng thái chờ xác nhận.
        var response = await SendCancellationAsync(code, anonymousId, tokenQuery: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        var data = json.GetProperty("data");
        Assert.Equal("cancelled", data.GetProperty("status").GetString());

        // Tồn kho được hoàn trả lại thành 10 trong catalog.
        Assert.Equal(10, await _kit.StockOfAsync(skuId));

        // Sự kiện đổi trạng thái đơn ghi nhận trạng thái trước là PendingConfirmation.
        var statusEvent = Assert.Single(
            _factory.DomainEvents.OfType<OrderStatusChangedEvent>(),
            e => e.Code == code && e.Status == OrderStatus.Cancelled);
        Assert.Equal(OrderStatus.PendingConfirmation, statusEvent.PreviousStatus);
    }

    // Tự hủy đơn chờ xác nhận đã thanh toán thì chuyển trạng thái hoàn tiền.
    [Fact]
    public async Task Cancel_WhenPaidPendingConfirmation_SetsPaymentStatusToRefundPending()
    {
        var customer = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var (code, _, _) = await CreateOrderAsync(anonymousId, sessionToken: customer.SessionToken);

        // Cập nhật trạng thái đơn thành chờ xác nhận và đã thanh toán trong cơ sở dữ liệu.
        await using (var commerce = _kit.Database.CreateCommerceContext())
        {
            var order = await commerce.Orders.SingleAsync(o => o.Code == code);
            order.Status = OrderStatus.PendingConfirmation;
            order.PaymentStatus = PaymentStatus.Paid;
            await commerce.SaveChangesAsync();
        }

        var response = await SendCancellationAsync(code, anonymousId, sessionToken: customer.SessionToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        var data = json.GetProperty("data");
        Assert.Equal("cancelled", data.GetProperty("status").GetString());
        Assert.Equal("refund_pending", data.GetProperty("paymentStatus").GetString());
    }

    // Tự hủy ở trạng thái không cho phép nhận lỗi 409 và không đổi kho.
    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipping)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Cancel_WhenStatusNotCancellableByCustomer_Returns409(OrderStatus invalidStatus)
    {
        var anonymousId = _kit.NewAnonymousId();
        var (code, token, skuId) = await CreateOrderAsync(anonymousId, quantity: 2, stock: 10);

        // Ghi thẳng trạng thái không cho phép khách hủy vào cơ sở dữ liệu.
        await using (var commerce = _kit.Database.CreateCommerceContext())
        {
            var order = await commerce.Orders.SingleAsync(o => o.Code == code);
            order.Status = invalidStatus;
            await commerce.SaveChangesAsync();
        }

        var eventsCountBefore = _factory.DomainEvents.OfType<OrderStatusChangedEvent>().Count(e => e.Code == code);

        var response = await SendCancellationAsync(code, anonymousId, tokenQuery: token);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        Assert.Equal("INVALID_ORDER_TRANSITION", json.GetProperty("error").GetProperty("code").GetString());

        // Tồn kho không bị thay đổi.
        Assert.Equal(8, await _kit.StockOfAsync(skuId));

        // Không phát thêm sự kiện đổi trạng thái nào cho đơn này.
        var eventsCountAfter = _factory.DomainEvents.OfType<OrderStatusChangedEvent>().Count(e => e.Code == code);
        Assert.Equal(eventsCountBefore, eventsCountAfter);
    }

    // Hai yêu cầu hủy đồng thời chỉ một yêu cầu thắng cuộc. Tồn kho hoàn trả đúng một lần.
    [Fact]
    public async Task Cancel_ConcurrentRequests_OnlyOneWinsAndStockReleasedOnce()
    {
        var anonymousId = _kit.NewAnonymousId();
        var (code, token, skuId) = await CreateOrderAsync(anonymousId, quantity: 2, stock: 10);

        // Gửi hai yêu cầu tự hủy cùng lúc.
        var task1 = SendCancellationAsync(code, anonymousId, tokenQuery: token);
        var task2 = SendCancellationAsync(code, anonymousId, tokenQuery: token);
        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToList();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, statusCodes);

        // Tồn kho chỉ được hoàn trả đúng 1 lần, từ 8 về 10 chứ không cộng dồn.
        Assert.Equal(10, await _kit.StockOfAsync(skuId));

        // Chỉ có đúng 1 sự kiện hủy đơn được phát.
        var cancelEvents = _factory.DomainEvents.OfType<OrderStatusChangedEvent>()
            .Where(e => e.Code == code && e.Status == OrderStatus.Cancelled)
            .ToList();
        Assert.Single(cancelEvents);
    }

    // Cả bốn route mới đều trả tiêu đề cấm lưu bộ nhớ đệm.
    [Fact]
    public async Task AllFourRoutes_ReturnNoStoreCacheControlHeader()
    {
        var customer = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var (code, token, _) = await CreateOrderAsync(anonymousId, sessionToken: customer.SessionToken);

        var r1 = await SendGetAsync(OrdersPath, anonymousId, sessionToken: customer.SessionToken);
        var r2 = await SendGetAsync($"{OrdersPath}/{code}", anonymousId, tokenQuery: token);
        var r3 = await SendGetAsync($"{OrdersPath}/{code}/payment", anonymousId, tokenQuery: token);
        var r4 = await SendCancellationAsync(code, anonymousId, tokenQuery: token);

        // Cả 4 route phải có tiêu đề cấm lưu bộ nhớ đệm.
        Assert.Equal("no-store", r1.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", r2.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", r3.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", r4.Headers.CacheControl?.ToString());
    }
}
