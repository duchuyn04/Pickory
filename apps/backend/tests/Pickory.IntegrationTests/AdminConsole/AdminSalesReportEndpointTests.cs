using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Commerce;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.AdminConsole;

// ADM-12: kiểm tra endpoint GET /api/admin/reports/sales qua DB thật và container thật.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminSalesReportEndpointTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string ReportsPath = "/api/admin/reports/sales";
    private static readonly DateTimeOffset TestClockInstant = new(2040, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestClockInstant);
    private readonly WebApplicationFactory<Program> _host;
    private readonly HttpClient _client;
    private readonly IntegrationDatabaseFixture _database;
    private readonly AdminOrderTestKit _orderKit;
    private readonly List<string> _catalogProductIds = [];
    private readonly List<string> _catalogBrandIds = [];
    private readonly List<string> _catalogCategoryIds = [];

    private string _salesCookie = null!;
    private string _supportCookie = null!;
    private string _customerCookie = null!;

    public AdminSalesReportEndpointTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Thay thế đồng hồ thật bằng đồng hồ giả để kiểm soát ngày hiện tại trong bài test.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_clock);
        }));
        _client = _host.CreateClient(new() { HandleCookies = false });
        _database = database;
        _orderKit = new AdminOrderTestKit(database);
    }

    public async Task InitializeAsync()
    {
        // Xóa sạch bảng đơn hàng để dữ liệu test trước không ảnh hưởng đến số liệu báo cáo.
        await _orderKit.ResetAsync();

        // Tạo phiên đăng nhập cho các vai trò để kiểm tra phân quyền.
        _salesCookie = await CreateSessionCookieAsync(UserKind.Staff, DefaultRoles.SalesCode);
        _supportCookie = await CreateSessionCookieAsync(UserKind.Staff, DefaultRoles.SupportCode);
        _customerCookie = await CreateSessionCookieAsync(UserKind.Customer, DefaultRoles.CustomerCode);
    }

    public async Task DisposeAsync()
    {
        // Dọn dẹp dữ liệu đơn hàng và danh mục sau khi kết thúc test.
        await _orderKit.ResetAsync();
        await CleanCatalogAsync();
        _host.Dispose();
    }

    [Fact]
    // Khách vãng lai chưa đăng nhập phải nhận mã lỗi 401.
    public async Task Get_Guest_Returns401Unauthorized()
    {
        var response = await GetAsync("range=30d", cookieHeader: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // Khách hàng thông thường không có quyền xem báo cáo phải nhận mã lỗi 403.
    public async Task Get_Customer_Returns403Forbidden()
    {
        var response = await GetAsync("range=30d", _customerCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Nhân viên không có quyền reports:read phải nhận mã lỗi 403.
    public async Task Get_StaffWithoutReportsRead_Returns403Forbidden()
    {
        var response = await GetAsync("range=30d", _supportCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Kiểm tra quyền chạy trước xác thực tham số nên query sai vẫn nhận 403.
    public async Task Get_StaffWithoutReportsRead_Gets403BeforeValidation()
    {
        var response = await GetAsync("from=invalid", _supportCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("range=invalid")]
    [InlineData("granularity=invalid")]
    [InlineData("range=custom&from=2040-05-10&to=2040-05-01")]
    [InlineData("range=custom&from=2040-01-01&to=2041-01-05")]
    [InlineData("range=month")]
    [InlineData("range=month&month=2040-5")]
    [InlineData("range=custom&from=abc&to=2040-05-10")]
    // Tham số truy vấn sai định dạng phải nhận mã lỗi 400 VALIDATION_ERROR.
    public async Task Get_InvalidQuery_Returns400ValidationError(string query)
    {
        var response = await GetAsync(query, _salesCookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var errorCode = document.RootElement.GetProperty("error").GetProperty("code").GetString();
        Assert.Equal("VALIDATION_ERROR", errorCode);
    }

    [Fact]
    // Khoảng thời gian không có đơn hàng nào vẫn trả về 200 với các tổng bằng 0 và averageOrderValue là null.
    public async Task Get_ValidRangeWithoutData_Returns200WithZeroTotalsAndBuckets()
    {
        var response = await GetAsync("range=custom&from=2040-01-01&to=2040-01-03&granularity=day", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");

        Assert.Equal("2040-01-01", data.GetProperty("from").GetString());
        Assert.Equal("2040-01-03", data.GetProperty("to").GetString());
        Assert.Equal("day", data.GetProperty("granularity").GetString());

        var totals = data.GetProperty("totals");
        Assert.Equal(0, totals.GetProperty("recognizedRevenue").GetInt64());
        Assert.Equal(0, totals.GetProperty("paidOrders").GetInt32());
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("averageOrderValue").ValueKind);
        Assert.Equal(0, totals.GetProperty("soldQuantity").GetInt32());
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("paidByMethod").GetProperty("codRate").ValueKind);
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("paidByMethod").GetProperty("sepayRate").ValueKind);
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("cancellations").GetProperty("byCustomerRate").ValueKind);
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("cancellations").GetProperty("byShopRate").ValueKind);
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("cancellations").GetProperty("paymentExpiredRate").ValueKind);

        var buckets = data.GetProperty("buckets").EnumerateArray().ToList();
        Assert.Equal(3, buckets.Count);
        Assert.All(buckets, bucket => Assert.Equal(0, bucket.GetProperty("recognizedRevenue").GetInt64()));
        Assert.Empty(data.GetProperty("topCategories").EnumerateArray());
    }

    [Fact]
    // Ngày báo cáo phải đổi lúc nửa đêm giờ Việt Nam, không đợi nửa đêm UTC.
    public async Task Get_SevenDayRangeAfterVietnamMidnight_UsesVietnamToday()
    {
        // Đặt đồng hồ sau nửa đêm Việt Nam để phát hiện route dùng ngày UTC.
        _clock.SetUtcNow(new DateTimeOffset(2040, 7, 1, 17, 30, 0, TimeSpan.Zero));

        var response = await GetAsync("range=7d", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("2040-06-26", data.GetProperty("from").GetString());
        Assert.Equal("2040-07-02", data.GetProperty("to").GetString());
    }

    [Fact]
    // Hai đơn qua nửa đêm Chủ nhật giờ Việt Nam phải thuộc hai tuần bắt đầu thứ Hai liên tiếp.
    public async Task Get_VietnamMidnightBoundary_SplitsOrdersAcrossWeekBuckets()
    {
        // Đơn lúc 23:59 Chủ nhật giờ Việt Nam phải thuộc tuần bắt đầu 2040-05-21.
        await CreateOrderWithPaymentAsync(
            "ORDER-WEEK-PREVIOUS",
            new DateTime(2040, 5, 27, 16, 59, 0, DateTimeKind.Utc),
            total: 500_000,
            lines: [("prod-week-previous", 2, 250_000)]);

        // Đơn lúc 00:00 thứ Hai giờ Việt Nam phải thuộc tuần bắt đầu 2040-05-28.
        await CreateOrderWithPaymentAsync(
            "ORDER-WEEK-NEXT",
            new DateTime(2040, 5, 27, 17, 0, 0, DateTimeKind.Utc),
            total: 300_000,
            lines: [("prod-week-next", 1, 300_000)]);

        var response = await GetAsync("range=custom&from=2040-05-21&to=2040-06-03&granularity=week", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var buckets = document.RootElement.GetProperty("data").GetProperty("buckets").EnumerateArray().ToList();
        Assert.Equal(2, buckets.Count);

        Assert.Equal("2040-05-21", buckets[0].GetProperty("key").GetString());
        Assert.Equal(500_000, buckets[0].GetProperty("recognizedRevenue").GetInt64());
        Assert.Equal(1, buckets[0].GetProperty("paidOrders").GetInt32());

        Assert.Equal("2040-05-28", buckets[1].GetProperty("key").GetString());
        Assert.Equal(300_000, buckets[1].GetProperty("recognizedRevenue").GetInt64());
        Assert.Equal(1, buckets[1].GetProperty("paidOrders").GetInt32());
    }

    [Fact]
    // Hai đơn thanh toán lúc 16:59 UTC và 17:00 UTC của ngày cuối tháng phải rơi vào hai tháng khác nhau.
    public async Task Get_VietnamMidnightBoundary_SplitsOrdersAcrossMonthBuckets()
    {
        // 16:59:00 UTC ngày 31/5 là 23:59:00 ngày 31/5 giờ Việt Nam, thuộc tháng 5.
        var paidMayUtc = new DateTime(2040, 5, 31, 16, 59, 0, DateTimeKind.Utc);
        await CreateOrderWithPaymentAsync(
            "ORDER-MAY",
            paidMayUtc,
            total: 500_000,
            lines: [("prod-may", 2, 250_000)]);

        // 17:00:00 UTC ngày 31/5 là 00:00:00 ngày 1/6 giờ Việt Nam, thuộc tháng 6.
        var paidJuneUtc = new DateTime(2040, 5, 31, 17, 0, 0, DateTimeKind.Utc);
        await CreateOrderWithPaymentAsync(
            "ORDER-JUNE",
            paidJuneUtc,
            total: 300_000,
            lines: [("prod-june", 1, 300_000)]);

        var response = await GetAsync("range=custom&from=2040-05-01&to=2040-06-30&granularity=month", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");

        var totals = data.GetProperty("totals");
        Assert.Equal(800_000, totals.GetProperty("recognizedRevenue").GetInt64());
        Assert.Equal(2, totals.GetProperty("paidOrders").GetInt32());
        Assert.Equal(3, totals.GetProperty("soldQuantity").GetInt32());
        Assert.Equal(400_000, totals.GetProperty("averageOrderValue").GetInt64());

        var buckets = data.GetProperty("buckets").EnumerateArray().ToList();
        Assert.Equal(2, buckets.Count);

        // Nhóm tháng 5 nhận đúng đơn thanh toán lúc 16:59 UTC.
        Assert.Equal("2040-05", buckets[0].GetProperty("key").GetString());
        Assert.Equal(500_000, buckets[0].GetProperty("recognizedRevenue").GetInt64());
        Assert.Equal(1, buckets[0].GetProperty("paidOrders").GetInt32());
        Assert.Equal(2, buckets[0].GetProperty("soldQuantity").GetInt32());

        // Nhóm tháng 6 nhận đúng đơn thanh toán lúc 17:00 UTC.
        Assert.Equal("2040-06", buckets[1].GetProperty("key").GetString());
        Assert.Equal(300_000, buckets[1].GetProperty("recognizedRevenue").GetInt64());
        Assert.Equal(1, buckets[1].GetProperty("paidOrders").GetInt32());
        Assert.Equal(1, buckets[1].GetProperty("soldQuantity").GetInt32());
    }

    [Fact]
    // Kiểm tra gộp thương hiệu và đưa nhóm null về cuối.
    public async Task Get_WithCatalogProducts_GroupsBrandsCorrectlyAndPutsNullBrandAtEnd()
    {
        var categoryId = await SeedCategoryAsync("Thiết bị");
        var brandAppleId = await SeedBrandAsync("Apple", "apple-test");
        var brandSonyId = await SeedBrandAsync("Sony", "sony-test");

        await SeedProductAsync("prod-apple", "iPhone 16", categoryId, brandAppleId);
        await SeedProductAsync("prod-sony", "Tai nghe Sony", categoryId, brandSonyId);
        // prod-unknown không đưa vào catalog để kiểm tra nhóm thương hiệu null

        var paidUtc = new DateTime(2040, 5, 10, 3, 0, 0, DateTimeKind.Utc);
        await CreateOrderWithPaymentAsync(
            "ORDER-BRANDS",
            paidUtc,
            total: 1_200_000,
            lines:
            [
                ("prod-apple", 1, 500_000),
                ("prod-sony", 2, 200_000),
                ("prod-unknown", 3, 100_000),
            ]);

        var response = await GetAsync("range=custom&from=2040-05-01&to=2040-05-31&granularity=month", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");

        var brands = data.GetProperty("brands").EnumerateArray().ToList();
        Assert.Equal(3, brands.Count);

        // Apple có doanh thu 500.000 cao nhất xếp đầu.
        Assert.Equal(brandAppleId, brands[0].GetProperty("brandId").GetString());
        Assert.Equal("Apple", brands[0].GetProperty("brandName").GetString());
        Assert.Equal(500_000, brands[0].GetProperty("revenue").GetInt64());
        Assert.Equal(1, brands[0].GetProperty("quantity").GetInt32());
        var appleProducts = brands[0].GetProperty("products").EnumerateArray().ToList();
        Assert.Single(appleProducts);
        Assert.Equal("prod-apple", appleProducts[0].GetProperty("productId").GetString());
        Assert.Equal("iPhone 16", appleProducts[0].GetProperty("name").GetString());
        Assert.Equal(500_000, appleProducts[0].GetProperty("revenue").GetInt64());
        Assert.Equal(1, appleProducts[0].GetProperty("quantity").GetInt32());

        // Sony có doanh thu 400.000 xếp thứ hai.
        Assert.Equal(brandSonyId, brands[1].GetProperty("brandId").GetString());
        Assert.Equal("Sony", brands[1].GetProperty("brandName").GetString());
        Assert.Equal(400_000, brands[1].GetProperty("revenue").GetInt64());
        Assert.Equal(2, brands[1].GetProperty("quantity").GetInt32());
        var sonyProducts = brands[1].GetProperty("products").EnumerateArray().ToList();
        Assert.Single(sonyProducts);
        Assert.Equal("prod-sony", sonyProducts[0].GetProperty("productId").GetString());
        Assert.Equal("Tai nghe Sony", sonyProducts[0].GetProperty("name").GetString());
        Assert.Equal(400_000, sonyProducts[0].GetProperty("revenue").GetInt64());
        Assert.Equal(2, sonyProducts[0].GetProperty("quantity").GetInt32());

        // Nhóm thương hiệu null luôn nằm ở cuối danh sách.
        Assert.Equal(JsonValueKind.Null, brands[2].GetProperty("brandId").ValueKind);
        Assert.Equal(JsonValueKind.Null, brands[2].GetProperty("brandName").ValueKind);
        Assert.Equal(300_000, brands[2].GetProperty("revenue").GetInt64());
        Assert.Equal(3, brands[2].GetProperty("quantity").GetInt32());
        var unknownProducts = brands[2].GetProperty("products").EnumerateArray().ToList();
        Assert.Single(unknownProducts);
        Assert.Equal("prod-unknown", unknownProducts[0].GetProperty("productId").GetString());
        Assert.Equal(JsonValueKind.Null, unknownProducts[0].GetProperty("name").ValueKind);
        Assert.Equal(300_000, unknownProducts[0].GetProperty("revenue").GetInt64());
        Assert.Equal(3, unknownProducts[0].GetProperty("quantity").GetInt32());

        // Danh mục lá gộp doanh thu của mọi sản phẩm trong danh mục. Nhóm null nằm cuối danh sách.
        var topCategories = data.GetProperty("topCategories").EnumerateArray().ToList();
        Assert.Equal(2, topCategories.Count);
        Assert.Equal(categoryId, topCategories[0].GetProperty("categoryId").GetString());
        Assert.Equal("Thiết bị", topCategories[0].GetProperty("categoryName").GetString());
        Assert.Equal(900_000, topCategories[0].GetProperty("revenue").GetInt64());
        Assert.Equal(3, topCategories[0].GetProperty("quantity").GetInt32());
        Assert.Equal(JsonValueKind.Null, topCategories[1].GetProperty("categoryId").ValueKind);
        Assert.Equal(300_000, topCategories[1].GetProperty("revenue").GetInt64());
        Assert.Equal(3, topCategories[1].GetProperty("quantity").GetInt32());

        // Top sản phẩm giữ đủ mọi sản phẩm và bổ sung thông tin từ catalog.
        var topProducts = data.GetProperty("topProducts").EnumerateArray().ToList();
        Assert.Equal(3, topProducts.Count);
        Assert.Equal("prod-apple", topProducts[0].GetProperty("productId").GetString());
        Assert.Equal("iPhone 16", topProducts[0].GetProperty("name").GetString());
        Assert.Equal("Apple", topProducts[0].GetProperty("brandName").GetString());
        Assert.Equal("prod-unknown", topProducts[2].GetProperty("productId").GetString());
        Assert.Equal(JsonValueKind.Null, topProducts[2].GetProperty("name").ValueKind);

        var totals = data.GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("orderStatuses").GetProperty("confirmed").GetInt32());
        Assert.Equal(0, totals.GetProperty("cancellations").GetProperty("byCustomerRate").GetDecimal());
        Assert.Equal(0, totals.GetProperty("cancellations").GetProperty("byShopRate").GetDecimal());
        Assert.Equal(0, totals.GetProperty("cancellations").GetProperty("paymentExpiredRate").GetDecimal());
        Assert.Equal(0, totals.GetProperty("paidByMethod").GetProperty("codRate").GetDecimal());
        Assert.Equal(1, totals.GetProperty("paidByMethod").GetProperty("sepayRate").GetDecimal());
        Assert.Equal(1_200_000, totals.GetProperty("placedOrderValue").GetInt64());
        Assert.Equal(0, totals.GetProperty("shippingDiscount").GetInt64());
    }

    [Fact]
    // Tổng và từng nhóm chỉ đếm đơn đã thanh toán theo phương thức. Đơn chưa thanh toán vẫn thuộc chỉ số đơn tạo.
    public async Task Get_WithPaidAndUnpaidOrders_CountsOnlyPaidMethodsInTotalsAndBuckets()
    {
        var paidAtUtc = new DateTime(2040, 5, 10, 3, 0, 0, DateTimeKind.Utc);
        // Đơn SePay đã thanh toán là đối chứng phải xuất hiện trong cả tổng và từng nhóm.
        await CreateOrderWithPaymentAsync(
            "ORDER-PAID-SEPAY",
            paidAtUtc,
            total: 300_000,
            lines: [("prod-paid-sepay", 1, 300_000)]);
        // Hai đơn chưa thanh toán phải được loại khỏi bộ đếm phương thức của cả COD và SePay.
        await _orderKit.AddOrderAsync(
            "ORDER-UNPAID-COD",
            paidAtUtc.AddMinutes(1),
            paymentMethod: PaymentMethod.Cod);
        await _orderKit.AddOrderAsync(
            "ORDER-UNPAID-SEPAY",
            paidAtUtc.AddMinutes(2),
            status: OrderStatus.PendingPayment,
            paymentMethod: PaymentMethod.Sepay);

        // Gọi endpoint trong cùng ngày để tổng và từng nhóm dùng chung bộ dữ liệu kiểm chứng.
        var response = await GetAsync("range=custom&from=2040-05-10&to=2040-05-10&granularity=day", _salesCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        var totals = data.GetProperty("totals");
        Assert.Equal(300_000, totals.GetProperty("recognizedRevenue").GetInt64());
        Assert.Equal(1, totals.GetProperty("paidOrders").GetInt32());
        Assert.Equal(3, totals.GetProperty("createdOrders").GetInt32());
        Assert.Equal(740_000, totals.GetProperty("placedOrderValue").GetInt64());
        Assert.Equal(20_000, totals.GetProperty("shippingDiscount").GetInt64());
        var totalMethods = totals.GetProperty("paidByMethod");
        Assert.Equal(0, totalMethods.GetProperty("cod").GetInt32());
        Assert.Equal(1, totalMethods.GetProperty("sepay").GetInt32());
        Assert.Equal(0, totalMethods.GetProperty("codRate").GetDecimal());
        Assert.Equal(1, totalMethods.GetProperty("sepayRate").GetDecimal());

        var bucket = Assert.Single(data.GetProperty("buckets").EnumerateArray());
        Assert.Equal(300_000, bucket.GetProperty("recognizedRevenue").GetInt64());
        Assert.Equal(1, bucket.GetProperty("paidOrders").GetInt32());
        var bucketMethods = bucket.GetProperty("paidByMethod");
        Assert.Equal(0, bucketMethods.GetProperty("cod").GetInt32());
        Assert.Equal(1, bucketMethods.GetProperty("sepay").GetInt32());
    }

    private async Task<HttpResponseMessage> GetAsync(string query, string? cookieHeader)
    {
        var path = query.Length == 0 ? ReportsPath : $"{ReportsPath}?{query}";
        using var request = ActorHttp.CreateRequest(HttpMethod.Get, path, cookieHeader);
        return await _client.SendAsync(request);
    }

    private async Task<string> CreateSessionCookieAsync(UserKind kind, string roleCode)
    {
        var user = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("sales-reports"), kind, roleCode, emailVerified: true);
        return $"pk_session={await IdentityTestData.CreateSessionAsync(_database, user.Id)}";
    }

    private async Task<Order> CreateOrderWithPaymentAsync(
        string code,
        DateTime paidAtUtc,
        int total,
        (string ProductId, int Quantity, int UnitPrice)[] lines)
    {
        await using var commerce = _database.CreateCommerceContext();
        DotnetSchemaIntegrationTests.AssertTestConnection(commerce);

        var order = new Order
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = code,
            AnonymousId = "anon-" + code,
            Contact = JsonDocument.Parse("{\"name\":\"Test Customer\",\"phone\":\"0901234567\"}"),
            ShippingAddress = JsonDocument.Parse("{\"provinceName\":\"Hà Nội\",\"line\":\"Test\"}"),
            PaymentMethod = PaymentMethod.Sepay,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            Total = total,
            Subtotal = total,
            ShippingFee = 0,
            ShippingDiscount = 0,
            CreatedAt = paidAtUtc.AddMinutes(-10),
            AccessTokenHash = "hash-" + code,
            IdempotencyKey = "key-" + code,
        };

        foreach (var line in lines)
        {
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid().ToString("N"),
                OrderId = order.Id,
                ProductId = line.ProductId,
                SkuId = "sku-" + line.ProductId,
                NameSnapshot = "Snapshot " + line.ProductId,
                SkuCodeSnapshot = "SKU-" + line.ProductId,
                OptionValues = JsonDocument.Parse("{}"),
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity,
            });
        }

        var transaction = new PaymentTransaction
        {
            Id = Guid.NewGuid().ToString("N"),
            OrderId = order.Id,
            Provider = "sepay",
            ProviderTxId = "txn-" + code,
            Amount = total,
            Content = code,
            Raw = JsonDocument.Parse("{}"),
            MatchStatus = "matched",
            ReceivedAt = paidAtUtc,
        };

        commerce.Orders.Add(order);
        commerce.PaymentTransactions.Add(transaction);
        await commerce.SaveChangesAsync();

        return order;
    }

    private async Task<string> SeedBrandAsync(string name, string slug)
    {
        await using var catalog = _database.CreateCatalogContext();
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Slug = slug + "-" + Guid.NewGuid().ToString("N")[..6],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        catalog.Brands.Add(brand);
        await catalog.SaveChangesAsync();
        _catalogBrandIds.Add(brand.Id);
        return brand.Id;
    }

    private async Task<string> SeedCategoryAsync(string name)
    {
        await using var catalog = _database.CreateCatalogContext();
        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Slug = "cat-" + Guid.NewGuid().ToString("N")[..6],
            Position = 0,
            Attributes = JsonDocument.Parse("[]"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        catalog.Categories.Add(category);
        await catalog.SaveChangesAsync();
        _catalogCategoryIds.Add(category.Id);
        return category.Id;
    }

    private async Task SeedProductAsync(string productId, string name, string categoryId, string brandId)
    {
        await using var catalog = _database.CreateCatalogContext();
        var product = new Product
        {
            Id = productId,
            Name = name,
            Slug = "prod-slug-" + productId,
            CategoryId = categoryId,
            BrandId = brandId,
            Status = ProductStatus.Active,
            Description = JsonDocument.Parse("""{"type":"doc","content":[{"type":"paragraph"}]}"""),
            Specs = JsonDocument.Parse("[]"),
            Options = JsonDocument.Parse("""[{"name":"Màu","values":["Đỏ","Xanh"]}]"""),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var sku = new Sku
        {
            Id = "sku-" + productId,
            ProductId = productId,
            Code = "SKU-CODE-" + productId,
            Price = 100_000,
            Stock = 10,
            Enabled = true,
            OptionValues = JsonDocument.Parse("{}"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        product.Skus.Add(sku);
        catalog.Products.Add(product);
        await catalog.SaveChangesAsync();
        _catalogProductIds.Add(productId);
    }

    private async Task CleanCatalogAsync()
    {
        await using var catalog = _database.CreateCatalogContext();
        if (_catalogProductIds.Count > 0)
        {
            await catalog.Skus.Where(sku => _catalogProductIds.Contains(sku.ProductId)).ExecuteDeleteAsync();
            await catalog.Products.Where(product => _catalogProductIds.Contains(product.Id)).ExecuteDeleteAsync();
        }

        if (_catalogBrandIds.Count > 0)
        {
            await catalog.Brands.Where(brand => _catalogBrandIds.Contains(brand.Id)).ExecuteDeleteAsync();
        }

        if (_catalogCategoryIds.Count > 0)
        {
            await catalog.Categories.Where(category => _catalogCategoryIds.Contains(category.Id)).ExecuteDeleteAsync();
        }
    }
}
