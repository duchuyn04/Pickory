using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Contracts.Reading;
using Pickory.Modules.Identity.Contracts.Customers;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra endpoint `GET /api/admin/support/sessions/{id}/customer` cho bảng thông tin khách (SD-11).
[Collection(SupportDeskCollection.Name)]
public sealed class AdminSupportSessionCustomerEndpointTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private readonly FakeCustomerDirectory _customerDirectory = new();
    private readonly FakeCommerceReader _commerceReader = new();
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        // Khởi tạo host test với dịch vụ giả lập danh bạ khách hàng và cổng đọc commerce.
        _host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICustomerDirectory>();
                services.AddSingleton<ICustomerDirectory>(_customerDirectory);
                services.RemoveAll<ICommerceReader>();
                services.AddSingleton<ICommerceReader>(_commerceReader);
            });
        });
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    private static string CustomerPath(string sessionId) =>
        $"/api/admin/support/sessions/{sessionId}/customer";

    [Fact]
    // Khách hàng đã đăng nhập phải trả về đủ thông tin tên, email và tối đa 5 đơn gần nhất.
    public async Task GetCustomerPanel_RegisteredCustomer_ReturnsProfileAndOrders()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var customerUserId = "usr-registered-1";
        var anonymousId = Guid.NewGuid().ToString();

        _customerDirectory.Handler = userId => new CustomerContact("Nguyễn Minh Anh", "minhanh@example.com");
        _commerceReader.CustomerOrdersHandler = (userId, limit) =>
        [
            new OrderSummary("PK123", OrderStatus.Shipping, PaymentStatus.Paid, PaymentMethod.Sepay, 459000, 2, DateTime.UtcNow)
        ];

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId, customerUserId: customerUserId);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, DateTime.UtcNow.AddMinutes(-10));

        var request = ActorHttp.CreateRequest(HttpMethod.Get, CustomerPath(session.Id), staffCookie);
        var response = await _client.SendAsync(request);

        // Kiểm tra mã trạng thái thành công `200 OK`.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var customer = body["data"]!["customer"]!;
        Assert.False(customer["isGuest"]!.GetValue<bool>());
        Assert.Equal("Nguyễn Minh Anh", customer["name"]!.GetValue<string>());
        Assert.Equal("minhanh@example.com", customer["email"]!.GetValue<string>());

        var orders = body["data"]!["orders"]!.AsArray();
        Assert.Single(orders);
        Assert.Equal("PK123", orders[0]!["code"]!.GetValue<string>());
        Assert.Equal("shipping", orders[0]!["status"]!.GetValue<string>());
        Assert.Equal(459000, orders[0]!["total"]!.GetValue<int>());
        Assert.Equal(2, orders[0]!["itemCount"]!.GetValue<int>());

        // Kiểm tra danh bạ `identity` được gọi đúng với mã người dùng của khách hàng.
        Assert.Contains(customerUserId, _customerDirectory.RecordedUserIds);

        // Kiểm tra `commerce` reader được gọi đúng phương thức của khách đăng nhập với giới hạn 5.
        var call = Assert.Single(_commerceReader.RecordedCustomerCalls);
        Assert.Equal(customerUserId, call.UserId);
        Assert.Equal(5, call.Limit);
        Assert.Empty(_commerceReader.RecordedAnonymousCalls);
    }

    [Fact]
    // Khách vãng lai phải có `isGuest` bằng `true`, tên và email bằng `null`, và chỉ đọc đơn qua mã ẩn danh.
    public async Task GetCustomerPanel_GuestCustomer_ReturnsGuestInfoAndAnonymousOrders()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var anonymousId = Guid.NewGuid().ToString();

        _commerceReader.AnonymousOrdersHandler = (anonId, limit) => [];

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId, customerUserId: null);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, DateTime.UtcNow.AddMinutes(-5));

        var request = ActorHttp.CreateRequest(HttpMethod.Get, CustomerPath(session.Id), staffCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var customer = body["data"]!["customer"]!;
        Assert.True(customer["isGuest"]!.GetValue<bool>());
        Assert.Null(customer["name"]);
        Assert.Null(customer["email"]);

        var orders = body["data"]!["orders"]!.AsArray();
        Assert.Empty(orders);

        // Không được gọi `ICustomerDirectory` khi khách hàng là khách vãng lai.
        Assert.Empty(_customerDirectory.RecordedUserIds);

        // Kiểm tra `commerce` reader được gọi qua `GetOrdersForAnonymousAsync` với đúng mã ẩn danh và giới hạn 5.
        var call = Assert.Single(_commerceReader.RecordedAnonymousCalls);
        Assert.Equal(anonymousId, call.AnonymousId);
        Assert.Equal(5, call.Limit);
        Assert.Empty(_commerceReader.RecordedCustomerCalls);
    }

    [Fact]
    // Khách hàng đã đăng nhập nhưng danh bạ không có hồ sơ phải trả về tên và email bằng `null`.
    public async Task GetCustomerPanel_RegisteredCustomerMissingContact_ReturnsNullNameAndEmail()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var customerUserId = "usr-not-in-directory";
        var anonymousId = Guid.NewGuid().ToString();

        _customerDirectory.Handler = userId => null;

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId, customerUserId: customerUserId);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, DateTime.UtcNow.AddMinutes(-5));

        var request = ActorHttp.CreateRequest(HttpMethod.Get, CustomerPath(session.Id), staffCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var customer = body["data"]!["customer"]!;
        Assert.False(customer["isGuest"]!.GetValue<bool>());
        Assert.Null(customer["name"]);
        Assert.Null(customer["email"]);
    }

    [Fact]
    // Trạng thái đơn hàng phải được chuyển thành `snake_case` và mỗi đơn phải có mã `code`.
    public async Task GetCustomerPanel_MultipleOrders_PreservesLimitAndSnakeCaseStatus()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var customerUserId = "usr-orders-check";
        var anonymousId = Guid.NewGuid().ToString();

        _customerDirectory.Handler = userId => new CustomerContact("Trần Nam", "nam@example.com");
        _commerceReader.CustomerOrdersHandler = (userId, limit) =>
        [
            new OrderSummary("ORD-1", OrderStatus.PendingPayment, PaymentStatus.Unpaid, PaymentMethod.Sepay, 100000, 1, DateTime.UtcNow),
            new OrderSummary("ORD-2", OrderStatus.PendingConfirmation, PaymentStatus.Unpaid, PaymentMethod.Cod, 200000, 2, DateTime.UtcNow),
            new OrderSummary("ORD-3", OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.Sepay, 300000, 3, DateTime.UtcNow),
            new OrderSummary("ORD-4", OrderStatus.Shipping, PaymentStatus.Paid, PaymentMethod.Sepay, 400000, 4, DateTime.UtcNow),
            new OrderSummary("ORD-5", OrderStatus.Delivered, PaymentStatus.Paid, PaymentMethod.Sepay, 500000, 5, DateTime.UtcNow),
        ];

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId, customerUserId: customerUserId);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, DateTime.UtcNow.AddMinutes(-5));

        var request = ActorHttp.CreateRequest(HttpMethod.Get, CustomerPath(session.Id), staffCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var orders = body["data"]!["orders"]!.AsArray();
        Assert.Equal(5, orders.Count);

        // Kiểm tra chuyển đổi các trạng thái đơn hàng sang định dạng snake_case.
        Assert.Equal("pending_payment", orders[0]!["status"]!.GetValue<string>());
        Assert.Equal("pending_confirmation", orders[1]!["status"]!.GetValue<string>());
        Assert.Equal("confirmed", orders[2]!["status"]!.GetValue<string>());
        Assert.Equal("shipping", orders[3]!["status"]!.GetValue<string>());
        Assert.Equal("delivered", orders[4]!["status"]!.GetValue<string>());

        // Kiểm tra mọi đơn hàng đều có mã `code` hợp lệ để frontend tạo liên kết quản trị.
        foreach (var orderNode in orders)
        {
            Assert.False(string.IsNullOrWhiteSpace(orderNode!["code"]!.GetValue<string>()));
        }
    }

    [Fact]
    // Phản hồi không được chứa các trường ngoài hợp đồng như `paymentStatus` hoặc sở thích.
    public async Task GetCustomerPanel_ResponseDoesNotContainUnwantedFields()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var customerUserId = "usr-clean-response";
        var anonymousId = Guid.NewGuid().ToString();

        _customerDirectory.Handler = userId => new CustomerContact("Lê Hoa", "hoa@example.com");
        _commerceReader.CustomerOrdersHandler = (userId, limit) =>
        [
            new OrderSummary("ORD-SECRET", OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.Sepay, 150000, 1, DateTime.UtcNow)
        ];

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId, customerUserId: customerUserId);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, DateTime.UtcNow.AddMinutes(-5));

        var request = ActorHttp.CreateRequest(HttpMethod.Get, CustomerPath(session.Id), staffCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        var data = body["data"]!.AsObject();
        var customer = data["customer"]!.AsObject();
        var order = data["orders"]!.AsArray()[0]!.AsObject();

        // Kiểm tra đối tượng customer chỉ chứa đúng ba trường quy định.
        Assert.Equal(3, customer.Count);
        Assert.True(customer.ContainsKey("isGuest"));
        Assert.True(customer.ContainsKey("name"));
        Assert.True(customer.ContainsKey("email"));

        // Kiểm tra đối tượng order chỉ chứa đúng năm trường quy định và không có `paymentStatus`.
        Assert.Equal(5, order.Count);
        Assert.True(order.ContainsKey("code"));
        Assert.True(order.ContainsKey("status"));
        Assert.True(order.ContainsKey("total"));
        Assert.True(order.ContainsKey("itemCount"));
        Assert.True(order.ContainsKey("createdAt"));
        Assert.False(order.ContainsKey("paymentStatus"));
        Assert.False(order.ContainsKey("preference"));
        Assert.False(order.ContainsKey("history"));
    }

    [Fact]
    // Mã phiên không tồn tại trong hệ thống phải trả về mã lỗi `404` và `SUPPORT_SESSION_NOT_FOUND`.
    public async Task GetCustomerPanel_NonExistentSession_Returns404NotFound()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var nonExistentId = Guid.NewGuid().ToString();

        var request = ActorHttp.CreateRequest(HttpMethod.Get, CustomerPath(nonExistentId), staffCookie);
        var response = await _client.SendAsync(request);

        // Kiểm tra mã trạng thái 404 và mã lỗi chuẩn của phiên hỗ trợ.
        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, SupportDeskErrorCodes.SessionNotFound);
    }

    [Fact]
    // Nhân viên vai `sales` không có quyền `conversations:read` phải nhận lỗi `403 Forbidden`.
    public async Task GetCustomerPanel_SalesStaff_Returns403Forbidden()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SalesCode, "customer-panel-sales");

        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, CustomerPath(Guid.NewGuid().ToString()), $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // Chặn người dùng thiếu quyền đọc hội thoại truy cập thông tin khách hàng.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Header `Cache-Control` của phản hồi phải chứa giá trị `no-store` để bảo vệ dữ liệu cá nhân.
    public async Task GetCustomerPanel_ResponseHasNoStoreHeader()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var anonymousId = Guid.NewGuid().ToString();

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId, customerUserId: null);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, DateTime.UtcNow.AddMinutes(-5));

        var request = ActorHttp.CreateRequest(HttpMethod.Get, CustomerPath(session.Id), staffCookie);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Kiểm tra cấu hình chống lưu đệm trên trình duyệt và proxy trung gian.
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    // Hợp đồng phản hồi khách đã đăng nhập phải khớp hoàn toàn ví dụ trong `admin-customer-panel.json`.
    public async Task Contract_RegisteredCustomer_MatchesFixtureExample()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var customerUserId = "usr-contract-registered";
        var anonymousId = Guid.NewGuid().ToString();

        _customerDirectory.Handler = userId => new CustomerContact("Nguyễn Minh Anh", "minhanh@example.com");
        _commerceReader.CustomerOrdersHandler = (userId, limit) =>
        [
            new OrderSummary("PK123", OrderStatus.Shipping, PaymentStatus.Paid, PaymentMethod.Sepay, 459000, 2, DateTime.Parse("2026-11-01T09:12:00Z"))
        ];

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId, customerUserId: customerUserId);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, DateTime.UtcNow.AddMinutes(-10));

        var request = ActorHttp.CreateRequest(HttpMethod.Get, CustomerPath(session.Id), staffCookie);
        var response = await _client.SendAsync(request);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body, response.StatusCode, "admin-customer-panel.json", "khách đã đăng nhập");
    }

    [Fact]
    // Hợp đồng phản hồi khách vãng lai phải khớp hoàn toàn ví dụ trong `admin-customer-panel.json`.
    public async Task Contract_GuestCustomer_MatchesFixtureExample()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var anonymousId = Guid.NewGuid().ToString();

        _commerceReader.AnonymousOrdersHandler = (anonId, limit) => [];

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, anonymousId, customerUserId: null);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, DateTime.UtcNow.AddMinutes(-5));

        var request = ActorHttp.CreateRequest(HttpMethod.Get, CustomerPath(session.Id), staffCookie);
        var response = await _client.SendAsync(request);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body, response.StatusCode, "admin-customer-panel.json", "khách vãng lai");
    }

    private sealed class FakeCustomerDirectory : ICustomerDirectory
    {
        public List<string> RecordedUserIds { get; } = [];
        public Func<string, CustomerContact?> Handler { get; set; } = _ => null;

        public Task<CustomerContact?> GetContactAsync(string userId, CancellationToken cancellationToken)
        {
            RecordedUserIds.Add(userId);
            return Task.FromResult(Handler(userId));
        }
    }

    private sealed class FakeCommerceReader : ICommerceReader
    {
        public List<(string UserId, int Limit)> RecordedCustomerCalls { get; } = [];
        public List<(string AnonymousId, int Limit)> RecordedAnonymousCalls { get; } = [];

        public Func<string, int, IReadOnlyList<OrderSummary>> CustomerOrdersHandler { get; set; } =
            (_, _) => [];

        public Func<string, int, IReadOnlyList<OrderSummary>> AnonymousOrdersHandler { get; set; } =
            (_, _) => [];

        public Task<IReadOnlyList<OrderSummary>> GetOrdersForCustomerAsync(
            string userId, int limit, CancellationToken cancellationToken)
        {
            RecordedCustomerCalls.Add((userId, limit));
            return Task.FromResult(CustomerOrdersHandler(userId, limit));
        }

        public Task<IReadOnlyList<OrderSummary>> GetOrdersForAnonymousAsync(
            string anonymousId, int limit, CancellationToken cancellationToken)
        {
            RecordedAnonymousCalls.Add((anonymousId, limit));
            return Task.FromResult(AnonymousOrdersHandler(anonymousId, limit));
        }

        public Task<IReadOnlyDictionary<string, RatingSummary>> GetRatingSummaryAsync(
            IReadOnlyList<string> productIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReviewPage> GetVisibleReviewsAsync(
            string productId, int limit, string? cursor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetPurchasedProductIdsAsync(
            string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OrderDetail?> GetOrderByCodeAsync(
            string code, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
