using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-19: đánh giá sản phẩm. Test đi qua route thật và đọc lại DB để chứng minh dữ liệu đã lưu.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ProductReviewTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string ReportsPath = "/api/reviews";
    private const string LockReviewsSql = "LOCK TABLE reviews IN SHARE ROW EXCLUSIVE MODE";
    private const string LockReportsSql = "LOCK TABLE review_reports IN SHARE ROW EXCLUSIVE MODE";

    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _userIds = [];
    private readonly List<string> _orderIds = [];
    private readonly List<string> _productIds = [];

    public ProductReviewTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new CartTestKit(factory, database);
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await _kit.InitializeAsync();
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public async Task DisposeAsync()
    {
        await using (var commerce = _database.CreateCommerceContext())
        {
            // Xóa đánh giá trước đơn vì khóa ngoại chặn xóa đơn còn đánh giá. Báo cáo xóa theo đánh giá.
            await commerce.Reviews.Where(review => _productIds.Contains(review.ProductId)).ExecuteDeleteAsync();
            await commerce.OrderItems.Where(item => _orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await commerce.Orders.Where(order => _orderIds.Contains(order.Id)).ExecuteDeleteAsync();
        }

        await _kit.CleanupAsync();
    }

    private string ReviewsPath(string productId) => $"/api/products/{productId}/reviews";

    private string MinePath(string productId) => $"/api/products/{productId}/reviews/mine";

    private sealed record Customer(string Id, string Cookie);

    private async Task<Customer> NewCustomerAsync()
    {
        var user = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("review"), UserKind.Customer, DefaultRoles.CustomerCode, emailVerified: true);
        _userIds.Add(user.Id);
        var token = await IdentityTestData.CreateSessionAsync(_database, user.Id);
        return new Customer(user.Id, $"pk_session={token}");
    }

    private async Task<string> NewProductAsync()
    {
        var product = await _kit.AddProductAsync();
        _productIds.Add(product.Id);
        return product.Id;
    }

    // Ghi thẳng một đơn có dòng hàng của `productId`, để điều khiển trạng thái và tên người nhận.
    private async Task<string> AddOrderAsync(
        string? userId, string productId, OrderStatus status = OrderStatus.Delivered,
        string recipientName = "Nguyễn Văn An", DateTime? createdAt = null)
    {
        var orderId = Guid.NewGuid().ToString();
        var code = "RV" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var order = new Order
        {
            Id = orderId,
            Code = code,
            UserId = userId,
            AnonymousId = "anon-" + code,
            Contact = JsonSerializer.SerializeToDocument(new
            {
                name = recipientName, phone = "0901234567", email = "an@example.com", note = "Giao giờ hành chính",
            }),
            ShippingAddress = JsonSerializer.SerializeToDocument(new
            {
                provinceCode = "1000001", provinceName = "Hồ Chí Minh", wardCode = "1003646", wardName = "Phường Bến Nghé", line = "12 Phố Test",
            }),
            PaymentMethod = PaymentMethod.Cod,
            Status = status,
            PaymentStatus = PaymentStatus.Paid,
            Flags = [],
            Subtotal = 200_000,
            ShippingFee = 30_000,
            Total = 230_000,
            AccessTokenHash = "hash-" + code,
            IdempotencyKey = "key-" + code,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid().ToString(),
            OrderId = orderId,
            SkuId = "sku-" + code,
            ProductId = productId,
            NameSnapshot = "Sản phẩm thử",
            SkuCodeSnapshot = "SKU-" + code,
            OptionValues = JsonSerializer.SerializeToDocument(new { Màu = "Đỏ" }),
            UnitPrice = 100_000,
            Quantity = 2,
        });

        await using var commerce = _database.CreateCommerceContext();
        commerce.Orders.Add(order);
        await commerce.SaveChangesAsync();
        _orderIds.Add(orderId);
        return orderId;
    }

    // Ghi thẳng một đánh giá để dựng nhiều dòng. Thời điểm tạo, trạng thái ẩn và người viết do test chọn.
    private async Task<string> SeedReviewAsync(
        string productId, string? userId, string orderId, int rating, DateTime? createdAt = null,
        DateTime? hiddenAt = null, string? body = null, string? id = null, int reportCount = 0)
    {
        var review = new Review
        {
            Id = id ?? Guid.NewGuid().ToString(),
            ProductId = productId,
            UserId = userId,
            OrderId = orderId,
            Rating = rating,
            Body = body,
            HiddenAt = hiddenAt,
            HiddenReason = hiddenAt is null ? null : "Vi phạm",
            ReportCount = reportCount,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = createdAt ?? DateTime.UtcNow,
        };
        await using var commerce = _database.CreateCommerceContext();
        commerce.Reviews.Add(review);
        await commerce.SaveChangesAsync();
        return review.Id;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, object? body = null)
    {
        return await _client.SendAsync(body is null && method == HttpMethod.Get
            ? ActorHttp.CreateRequest(method, path, cookie)
            : ActorHttp.CreateJsonRequest(method, path, body, cookie));
    }

    // Gửi chuỗi JSON thô, để test body mà serializer của test không tạo ra được (kiểu sai, mảng).
    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, string cookie, string json)
    {
        var request = ActorHttp.CreateRequest(method, path, cookie);
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var document = await ActorHttp.ReadJsonAsync(response);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, string? field = null)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        if (field is not null)
        {
            Assert.Contains(field + ":", error.GetProperty("message").GetString());
        }
    }

    private async Task<List<Review>> ReviewsOfAsync(string productId)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Reviews.AsNoTracking().Where(review => review.ProductId == productId).ToListAsync();
    }

    private async Task<Review> ReviewAsync(string reviewId)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Reviews.AsNoTracking().SingleAsync(review => review.Id == reviewId);
    }

    private async Task<int> ReportRowsAsync(string reviewId)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.ReviewReports.CountAsync(report => report.ReviewId == reviewId);
    }

    private int EventCount(string reviewId, ReviewChange change) =>
        _factory.DomainEvents.OfType<ReviewChangedEvent>().Count(e => e.ReviewId == reviewId && e.Change == change);

    private async Task SetCreatedAtAsync(string reviewId, DateTime createdAt)
    {
        await using var commerce = _database.CreateCommerceContext();
        await commerce.Reviews.Where(review => review.Id == reviewId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(review => review.CreatedAt, createdAt));
    }

    // Giữ khóa bảng ở chế độ chặn ghi nhưng không chặn đọc. Request đọc kiểm tra xong rồi cùng chờ ở bước ghi.
    private async Task<(Task<HttpResponseMessage[]> Pending, bool FinishedWhileLocked)> RunWhileTableLockedAsync(
        string lockSql, params Func<Task<HttpResponseMessage>>[] requests)
    {
        await using var holder = _database.CreateCommerceContext();
        await using var holderTransaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync(lockSql);

        var pending = Task.WhenAll(requests.Select(request => request()));
        var finishedWhileLocked = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2))) == pending;

        await holderTransaction.CommitAsync();
        await pending;
        return (pending, finishedWhileLocked);
    }

    // ---------- Danh sách công khai ----------

    [Fact]
    // Tóm tắt chỉ tính đánh giá đang hiện. Điểm trung bình làm tròn 1 chữ số và phân bố đủ năm khóa.
    public async Task List_ReturnsSummaryOfVisibleReviewsOnly()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(null, productId);
        await SeedReviewAsync(productId, "user-a", orderId, 5);
        await SeedReviewAsync(productId, "user-b", orderId, 4);
        await SeedReviewAsync(productId, "user-c", orderId, 4);
        // Đánh giá đã ẩn không được tính vào số lượng, điểm trung bình hay phân bố.
        await SeedReviewAsync(productId, "user-d", orderId, 1, hiddenAt: DateTime.UtcNow);

        var response = await SendAsync(HttpMethod.Get, ReviewsPath(productId), cookie: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        var summary = data.GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("count").GetInt32());
        Assert.Equal(4.3, summary.GetProperty("average").GetDouble());
        var distribution = summary.GetProperty("distribution");
        Assert.Equal(0, distribution.GetProperty("1").GetInt32());
        Assert.Equal(0, distribution.GetProperty("2").GetInt32());
        Assert.Equal(0, distribution.GetProperty("3").GetInt32());
        Assert.Equal(2, distribution.GetProperty("4").GetInt32());
        Assert.Equal(1, distribution.GetProperty("5").GetInt32());
        Assert.Equal(3, data.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    // Sản phẩm chưa có đánh giá: điểm trung bình `null`, số lượng 0, đủ năm khóa phân bố.
    public async Task List_WithoutReviews_ReturnsNullAverageAndZeroDistribution()
    {
        var productId = await NewProductAsync();

        var data = await ReadDataAsync(await SendAsync(HttpMethod.Get, ReviewsPath(productId), cookie: null));

        var summary = data.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("average").ValueKind);
        Assert.Equal(5, summary.GetProperty("distribution").EnumerateObject().Count());
        Assert.Empty(data.GetProperty("items").EnumerateArray());
    }

    [Fact]
    // Dòng công khai chỉ có sáu trường, tên rút gọn, và không lộ `userId`, email, số điện thoại hay `orderId`.
    public async Task List_Row_HasShortAuthorNameAndNoPrivateFields()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var orderId = await AddOrderAsync(customer.Id, productId, recipientName: "Nguyễn Văn An");
        var reviewId = await SeedReviewAsync(productId, customer.Id, orderId, 5, body: "Dùng tốt");

        var response = await SendAsync(HttpMethod.Get, ReviewsPath(productId), cookie: null);

        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        var row = document.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(reviewId, row.GetProperty("id").GetString());
        Assert.Equal(5, row.GetProperty("rating").GetInt32());
        Assert.Equal("Dùng tốt", row.GetProperty("body").GetString());
        Assert.Equal("An N.", row.GetProperty("authorName").GetString());
        Assert.Equal(
            ["authorName", "body", "createdAt", "id", "rating", "updatedAt"],
            row.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToList());
        Assert.DoesNotContain(customer.Id, raw);
        Assert.DoesNotContain(orderId, raw);
        Assert.DoesNotContain("an@example.com", raw);
        Assert.DoesNotContain("0901234567", raw);
    }

    [Fact]
    // Đánh giá đã ẩn danh (`UserId` null) hiện nhãn chung, dù đơn gắn kèm vẫn còn tên người nhận.
    public async Task List_AnonymizedReview_ShowsGenericName()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(null, productId, recipientName: "Trần Thị Bình");
        await SeedReviewAsync(productId, userId: null, orderId, 4);

        var data = await ReadDataAsync(await SendAsync(HttpMethod.Get, ReviewsPath(productId), cookie: null));

        Assert.Equal("Khách hàng", data.GetProperty("items")[0].GetProperty("authorName").GetString());
    }

    [Fact]
    // Hai trang cursor liền nhau không lặp và không mất dòng. Hai dòng cùng `CreatedAt` được chốt thứ tự theo `Id`.
    public async Task List_CursorPages_DoNotRepeatOrSkipRows()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(null, productId);
        var baseTime = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        // Ba dòng cùng thời điểm, ở giữa danh sách, để thứ tự phụ theo `Id` quyết định ranh giới trang.
        var sameTime = baseTime.AddMinutes(10);
        var expected = new List<string>
        {
            await SeedReviewAsync(productId, "u1", orderId, 5, baseTime.AddMinutes(20), id: "rv-new"),
            await SeedReviewAsync(productId, "u2", orderId, 5, sameTime, id: "rv-b"),
            await SeedReviewAsync(productId, "u3", orderId, 5, sameTime, id: "rv-a"),
            await SeedReviewAsync(productId, "u4", orderId, 5, sameTime, id: "rv-c"),
            await SeedReviewAsync(productId, "u5", orderId, 5, baseTime, id: "rv-old"),
        };
        // Thứ tự mong đợi: mới nhất trước, rồi theo `Id` tăng dần khi cùng thời điểm.
        var ordered = new[] { "rv-new", "rv-a", "rv-b", "rv-c", "rv-old" };

        var collected = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var query = cursor is null ? "limit=2" : $"limit=2&cursor={cursor}";
            var data = await ReadDataAsync(await SendAsync(HttpMethod.Get, $"{ReviewsPath(productId)}?{query}", cookie: null));
            collected.AddRange(data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!));
            var next = data.GetProperty("nextCursor");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(ordered, collected);
        Assert.Equal(expected.OrderBy(id => id, StringComparer.Ordinal).ToList(), collected.OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    [Fact]
    // Đánh giá ẩn nằm giữa hai trang không làm lệch cursor và không hiện trong trang nào.
    public async Task List_CursorPages_SkipHiddenReviews()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(null, productId);
        var baseTime = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        await SeedReviewAsync(productId, "u1", orderId, 5, baseTime.AddMinutes(3), id: "rv-1");
        await SeedReviewAsync(productId, "u2", orderId, 5, baseTime.AddMinutes(2), hiddenAt: DateTime.UtcNow, id: "rv-hidden");
        await SeedReviewAsync(productId, "u3", orderId, 5, baseTime.AddMinutes(1), id: "rv-2");

        var first = await ReadDataAsync(await SendAsync(HttpMethod.Get, $"{ReviewsPath(productId)}?limit=1", cookie: null));
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await ReadDataAsync(await SendAsync(HttpMethod.Get, $"{ReviewsPath(productId)}?limit=1&cursor={cursor}", cookie: null));

        Assert.Equal("rv-1", first.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal("rv-2", second.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("cursor=khong-phai-cursor", "cursor")]
    [InlineData("cursor=YWJj", "cursor")]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=51", "limit")]
    [InlineData("limit=abc", "limit")]
    [InlineData("limit=1&limit=2", "limit")]
    [InlineData("cursor=a&cursor=b", "cursor")]
    // Tham số sai bị từ chối bằng 400 đúng trường, không bị bỏ qua âm thầm.
    public async Task List_InvalidQuery_Returns400ForTheField(string query, string field)
    {
        var productId = await NewProductAsync();

        var response = await SendAsync(HttpMethod.Get, $"{ReviewsPath(productId)}?{query}", cookie: null);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", field);
    }

    [Fact]
    // Giới hạn mặc định là 10 dòng, và tối đa 50 được nhận.
    public async Task List_DefaultLimitIsTen_AndFiftyIsAccepted()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(null, productId);
        for (var index = 0; index < 12; index++)
        {
            await SeedReviewAsync(productId, $"u{index}", orderId, 5);
        }

        var byDefault = await ReadDataAsync(await SendAsync(HttpMethod.Get, ReviewsPath(productId), cookie: null));
        var atMax = await SendAsync(HttpMethod.Get, $"{ReviewsPath(productId)}?limit=50", cookie: null);

        Assert.Equal(10, byDefault.GetProperty("items").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, byDefault.GetProperty("nextCursor").ValueKind);
        Assert.Equal(HttpStatusCode.OK, atMax.StatusCode);
        Assert.Equal(12, (await ReadDataAsync(atMax)).GetProperty("items").GetArrayLength());
    }

    [Fact]
    // Mã sản phẩm không có trong catalog nhận 404 `PRODUCT_NOT_FOUND`.
    public async Task List_UnknownProduct_Returns404()
    {
        var response = await SendAsync(HttpMethod.Get, ReviewsPath("khong-co-san-pham"), cookie: null);

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    // ---------- Đánh giá của tôi ----------

    [Fact]
    // Chưa có đơn nào chứa sản phẩm: không được viết, lý do `NOT_PURCHASED`.
    public async Task Mine_WithoutOrder_IsNotEligible()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();

        var response = await SendAsync(HttpMethod.Get, MinePath(productId), customer.Cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.False(data.GetProperty("eligible").GetBoolean());
        Assert.Equal("NOT_PURCHASED", data.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("review").ValueKind);
    }

    [Fact]
    // Đơn chứa sản phẩm nhưng chưa `delivered` (đang giao) không đủ điều kiện.
    public async Task Mine_WithOnlyShippingOrder_IsNotEligible()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, productId, OrderStatus.Shipping);

        var data = await ReadDataAsync(await SendAsync(HttpMethod.Get, MinePath(productId), customer.Cookie));

        Assert.False(data.GetProperty("eligible").GetBoolean());
        Assert.Equal("NOT_PURCHASED", data.GetProperty("reason").GetString());
    }

    [Fact]
    // Có đơn `delivered` chứa sản phẩm và chưa đánh giá: được viết, `reason` là `null`.
    public async Task Mine_WithDeliveredOrder_IsEligible()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, productId);

        var data = await ReadDataAsync(await SendAsync(HttpMethod.Get, MinePath(productId), customer.Cookie));

        Assert.True(data.GetProperty("eligible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("reason").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("review").ValueKind);
    }

    [Fact]
    // Đã đánh giá: không được viết thêm, lý do `ALREADY_REVIEWED`. Đánh giá bị ẩn vẫn trả về kèm `hidden: true`.
    public async Task Mine_WithReview_ReturnsItEvenWhenHidden()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var orderId = await AddOrderAsync(customer.Id, productId);
        var createdAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var reviewId = await SeedReviewAsync(productId, customer.Id, orderId, 3, createdAt, hiddenAt: DateTime.UtcNow, body: "Tạm được");

        var data = await ReadDataAsync(await SendAsync(HttpMethod.Get, MinePath(productId), customer.Cookie));

        Assert.False(data.GetProperty("eligible").GetBoolean());
        Assert.Equal("ALREADY_REVIEWED", data.GetProperty("reason").GetString());
        var review = data.GetProperty("review");
        Assert.Equal(reviewId, review.GetProperty("id").GetString());
        Assert.Equal(3, review.GetProperty("rating").GetInt32());
        Assert.True(review.GetProperty("hidden").GetBoolean());
        var editableUntil = review.GetProperty("editableUntil").GetDateTime().ToUniversalTime();
        Assert.Equal(createdAt.AddDays(30), editableUntil);
    }

    [Fact]
    // Đơn `delivered` của khách khác hoặc của sản phẩm khác không làm khách này đủ điều kiện.
    public async Task Mine_IgnoresOrdersOfOtherCustomersAndOtherProducts()
    {
        var productId = await NewProductAsync();
        var otherProductId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var other = await NewCustomerAsync();
        await AddOrderAsync(other.Id, productId);
        await AddOrderAsync(customer.Id, otherProductId);

        var data = await ReadDataAsync(await SendAsync(HttpMethod.Get, MinePath(productId), customer.Cookie));

        Assert.False(data.GetProperty("eligible").GetBoolean());
        Assert.Equal("NOT_PURCHASED", data.GetProperty("reason").GetString());
    }

    [Fact]
    // Sản phẩm không tồn tại nhận 404 trước khi xét điều kiện.
    public async Task Mine_UnknownProduct_Returns404()
    {
        var customer = await NewCustomerAsync();

        var response = await SendAsync(HttpMethod.Get, MinePath("khong-co-san-pham"), customer.Cookie);

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    // ---------- Quyền truy cập ----------

    [Theory]
    [InlineData("GET", "mine")]
    [InlineData("POST", "")]
    [InlineData("PATCH", "mine")]
    // Chưa đăng nhập: route cá nhân và route ghi nhận 401 trước khi chạm logic.
    public async Task PersonalRoutes_WithoutSession_Return401(string method, string suffix)
    {
        var productId = await NewProductAsync();
        var path = suffix.Length == 0 ? ReviewsPath(productId) : $"{ReviewsPath(productId)}/{suffix}";

        var response = await SendAsync(new HttpMethod(method), path, cookie: null, new { rating = 5 });

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Theory]
    [InlineData("GET", "mine")]
    [InlineData("POST", "")]
    [InlineData("PATCH", "mine")]
    // Nhân viên có phiên hợp lệ nhưng không được viết, sửa hay xem đánh giá của khách.
    public async Task PersonalRoutes_WithStaffSession_Return403(string method, string suffix)
    {
        var productId = await NewProductAsync();
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(_database, "support");
        _userIds.Add(staff.Id);
        var path = suffix.Length == 0 ? ReviewsPath(productId) : $"{ReviewsPath(productId)}/{suffix}";

        var response = await SendAsync(new HttpMethod(method), path, $"pk_session={token}", new { rating = 5 });

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Empty(await ReviewsOfAsync(productId));
    }

    // ---------- Viết đánh giá ----------

    [Fact]
    // Khách có đơn `delivered` viết được đánh giá. Đánh giá hiện ngay, gắn với đơn mới nhất, và phát một sự kiện `Created`.
    public async Task Create_WithDeliveredOrder_Returns201AndShowsImmediately()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, productId, createdAt: DateTime.UtcNow.AddDays(-10));
        var newestOrderId = await AddOrderAsync(customer.Id, productId, createdAt: DateTime.UtcNow.AddDays(-2));
        // Đơn mới hơn nhưng chưa giao thì không được chọn.
        await AddOrderAsync(customer.Id, productId, OrderStatus.Shipping, createdAt: DateTime.UtcNow.AddDays(-1));

        var response = await SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie,
            new { rating = 4, body = "  Máy chạy êm  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.Equal(4, data.GetProperty("rating").GetInt32());
        Assert.Equal("Máy chạy êm", data.GetProperty("body").GetString());
        Assert.False(data.GetProperty("hidden").GetBoolean());
        var reviewId = data.GetProperty("id").GetString()!;

        var row = Assert.Single(await ReviewsOfAsync(productId));
        Assert.Equal(reviewId, row.Id);
        Assert.Equal(customer.Id, row.UserId);
        Assert.Equal(newestOrderId, row.OrderId);
        Assert.Null(row.HiddenAt);
        Assert.Equal(1, EventCount(reviewId, ReviewChange.Created));

        // Đánh giá hiện ngay và tóm tắt tính lại ở lần đọc sau.
        var list = await ReadDataAsync(await SendAsync(HttpMethod.Get, ReviewsPath(productId), cookie: null));
        Assert.Equal(1, list.GetProperty("summary").GetProperty("count").GetInt32());
        Assert.Equal(4.0, list.GetProperty("summary").GetProperty("average").GetDouble());
        Assert.Equal(reviewId, list.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    // `body` bỏ qua hoặc toàn khoảng trắng được lưu thành `null`.
    public async Task Create_WithoutBodyOrBlankBody_StoresNull()
    {
        var firstProduct = await NewProductAsync();
        var secondProduct = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, firstProduct);
        await AddOrderAsync(customer.Id, secondProduct);

        var withoutBody = await SendAsync(HttpMethod.Post, ReviewsPath(firstProduct), customer.Cookie, new { rating = 5 });
        var blankBody = await SendAsync(HttpMethod.Post, ReviewsPath(secondProduct), customer.Cookie, new { rating = 5, body = "   " });

        Assert.Equal(HttpStatusCode.Created, withoutBody.StatusCode);
        Assert.Equal(HttpStatusCode.Created, blankBody.StatusCode);
        Assert.Null(Assert.Single(await ReviewsOfAsync(firstProduct)).Body);
        Assert.Null(Assert.Single(await ReviewsOfAsync(secondProduct)).Body);
    }

    [Fact]
    // `body` đúng 2.000 ký tự được nhận. Biên 2.001 bị từ chối ở test bên dưới.
    public async Task Create_BodyOfExactlyLimit_Returns201()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, productId);

        var response = await SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie,
            new { rating = 5, body = new string('a', 2000) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    // Chưa có đơn nào: 403 `REVIEW_NOT_ALLOWED` và không tạo dòng nào.
    public async Task Create_WithoutAnyOrder_Returns403()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();

        var response = await SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie, new { rating = 5 });

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "REVIEW_NOT_ALLOWED");
        Assert.Empty(await ReviewsOfAsync(productId));
    }

    [Theory]
    [InlineData(OrderStatus.Shipping)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PendingConfirmation)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Returned)]
    // Đơn ở bất kỳ trạng thái nào khác `delivered` đều không đủ điều kiện, kể cả đơn đang giao.
    public async Task Create_WithOrderNotDelivered_Returns403(OrderStatus status)
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, productId, status);

        var response = await SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie, new { rating = 5 });

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "REVIEW_NOT_ALLOWED");
        Assert.Empty(await ReviewsOfAsync(productId));
    }

    [Fact]
    // Đơn `delivered` của khách khác không cho phép khách này viết.
    public async Task Create_WithOtherCustomersDeliveredOrder_Returns403()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var other = await NewCustomerAsync();
        await AddOrderAsync(other.Id, productId);

        var response = await SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie, new { rating = 5 });

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "REVIEW_NOT_ALLOWED");
    }

    [Fact]
    // Đơn `delivered` chỉ chứa sản phẩm khác không cho phép đánh giá sản phẩm này.
    public async Task Create_WithDeliveredOrderOfAnotherProduct_Returns403()
    {
        var productId = await NewProductAsync();
        var otherProductId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, otherProductId);

        var response = await SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie, new { rating = 5 });

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "REVIEW_NOT_ALLOWED");
    }

    [Fact]
    // Đánh giá thứ hai của cùng khách cho cùng sản phẩm nhận 409. Đánh giá đầu giữ nguyên, và không phát thêm sự kiện.
    public async Task Create_Second_Returns409AndKeepsFirst()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, productId);
        var first = await SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie, new { rating = 5, body = "Đầu tiên" });
        var firstId = (await ReadDataAsync(first)).GetProperty("id").GetString()!;

        var second = await SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie, new { rating = 1, body = "Thứ hai" });

        await AssertErrorAsync(second, HttpStatusCode.Conflict, "REVIEW_ALREADY_EXISTS");
        var row = Assert.Single(await ReviewsOfAsync(productId));
        Assert.Equal(firstId, row.Id);
        Assert.Equal("Đầu tiên", row.Body);
        Assert.Equal(1, _factory.DomainEvents.OfType<ReviewChangedEvent>().Count(e => e.ProductId == productId));
    }

    [Fact]
    // Hai `POST` đồng thời cùng qua bước kiểm tra chưa có đánh giá, rồi cùng chờ ở bước ghi.
    // Khóa duy nhất cho một request thắng, và request thua nhận 409 chứ không phải lỗi 500.
    public async Task Create_Concurrent_ReturnsOne201AndOne409()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, productId);

        var (pending, finishedWhileLocked) = await RunWhileTableLockedAsync(LockReviewsSql,
            () => SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie, new { rating = 5, body = "A" }),
            () => SendAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie, new { rating = 4, body = "B" }));

        Assert.False(finishedWhileLocked, "Request ghi phải chờ khóa bảng ở bước ghi.");
        var responses = await pending;
        var statusCodes = responses.Select(response => (int)response.StatusCode).OrderBy(code => code).ToList();
        Assert.Equal([201, 409], statusCodes);
        var loser = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertErrorAsync(loser, HttpStatusCode.Conflict, "REVIEW_ALREADY_EXISTS");
        var row = Assert.Single(await ReviewsOfAsync(productId));
        Assert.Equal(1, EventCount(row.Id, ReviewChange.Created));
    }

    [Fact]
    // Sản phẩm không tồn tại nhận 404 trước khi xét điều kiện viết.
    public async Task Create_UnknownProduct_Returns404()
    {
        var customer = await NewCustomerAsync();

        var response = await SendAsync(HttpMethod.Post, ReviewsPath("khong-co-san-pham"), customer.Cookie, new { rating = 5 });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    [Theory]
    [InlineData("""{"rating":0}""", "rating")]
    [InlineData("""{"rating":6}""", "rating")]
    [InlineData("""{"rating":3.5}""", "rating")]
    [InlineData("""{"rating":"5"}""", "rating")]
    [InlineData("""{"rating":null}""", "rating")]
    [InlineData("""{"rating":true}""", "rating")]
    [InlineData("""{"body":"thiếu sao"}""", "rating")]
    [InlineData("""{"rating":5,"body":5}""", "body")]
    [InlineData("""{"rating":5,"body":"__LONG__"}""", "body")]
    [InlineData("""{"rating":5,"color":"red"}""", "color")]
    [InlineData("""[]""", "")]
    // Body sai theo từng trường nhận 400 đúng trường và không tạo dòng nào. Kiểm tra chạy trước khi xét đơn hàng.
    public async Task Create_InvalidBody_Returns400ForTheField(string json, string field)
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        await AddOrderAsync(customer.Id, productId);
        var body = json.Replace("__LONG__", new string('a', 2001));

        var response = await SendRawAsync(HttpMethod.Post, ReviewsPath(productId), customer.Cookie, body);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", field.Length == 0 ? null : field);
        Assert.Empty(await ReviewsOfAsync(productId));
    }

    // ---------- Sửa đánh giá ----------

    [Fact]
    // Sửa trong 30 ngày: đổi số sao và nhận xét, `UpdatedAt` đổi, `CreatedAt` giữ nguyên, phát một sự kiện `Updated`.
    public async Task Patch_WithinThirtyDays_UpdatesReview()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var orderId = await AddOrderAsync(customer.Id, productId);
        var createdAt = DateTime.UtcNow.AddDays(-29).AddHours(-23);
        var reviewId = await SeedReviewAsync(productId, customer.Id, orderId, 5, createdAt, body: "Cũ");

        var response = await SendAsync(new HttpMethod("PATCH"), MinePath(productId), customer.Cookie,
            new { rating = 2, body = "Mới" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.Equal(2, data.GetProperty("rating").GetInt32());
        var row = await ReviewAsync(reviewId);
        Assert.Equal(2, row.Rating);
        Assert.Equal("Mới", row.Body);
        Assert.True(row.UpdatedAt > createdAt.AddHours(1));
        Assert.Equal(1, EventCount(reviewId, ReviewChange.Updated));
    }

    [Fact]
    // Chỉ gửi `rating` thì nhận xét giữ nguyên. Gửi `body: null` thì xóa nhận xét.
    public async Task Patch_ChangesOnlyGivenFields_AndNullBodyClearsIt()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var orderId = await AddOrderAsync(customer.Id, productId);
        var reviewId = await SeedReviewAsync(productId, customer.Id, orderId, 5, body: "Giữ lại");

        await SendAsync(new HttpMethod("PATCH"), MinePath(productId), customer.Cookie, new { rating = 3 });
        var afterRating = await ReviewAsync(reviewId);
        await SendRawAsync(new HttpMethod("PATCH"), MinePath(productId), customer.Cookie, """{"body":null}""");
        var afterClear = await ReviewAsync(reviewId);

        Assert.Equal(3, afterRating.Rating);
        Assert.Equal("Giữ lại", afterRating.Body);
        Assert.Equal(3, afterClear.Rating);
        Assert.Null(afterClear.Body);
    }

    [Fact]
    // Sửa không đổi `HiddenAt`: đánh giá đã bị ẩn vẫn ẩn sau khi khách sửa.
    public async Task Patch_DoesNotChangeHiddenAt()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var orderId = await AddOrderAsync(customer.Id, productId);
        var hiddenAt = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        var reviewId = await SeedReviewAsync(productId, customer.Id, orderId, 5, hiddenAt: hiddenAt);

        var response = await SendAsync(new HttpMethod("PATCH"), MinePath(productId), customer.Cookie, new { rating = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(hiddenAt, (await ReviewAsync(reviewId)).HiddenAt);
    }

    [Fact]
    // Quá 30 ngày kể từ `CreatedAt`: 409 `REVIEW_EDIT_EXPIRED`, không đổi dữ liệu và không phát sự kiện.
    public async Task Patch_AfterThirtyDays_Returns409AndChangesNothing()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var orderId = await AddOrderAsync(customer.Id, productId);
        var reviewId = await SeedReviewAsync(productId, customer.Id, orderId, 5, DateTime.UtcNow.AddDays(-30).AddMinutes(-5), body: "Cũ");

        var response = await SendAsync(new HttpMethod("PATCH"), MinePath(productId), customer.Cookie, new { rating = 1, body = "Mới" });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "REVIEW_EDIT_EXPIRED");
        var row = await ReviewAsync(reviewId);
        Assert.Equal(5, row.Rating);
        Assert.Equal("Cũ", row.Body);
        Assert.Equal(0, EventCount(reviewId, ReviewChange.Updated));
    }

    [Fact]
    // Khách chưa có đánh giá cho sản phẩm nhận 404.
    public async Task Patch_WithoutReview_Returns404()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();

        var response = await SendAsync(new HttpMethod("PATCH"), MinePath(productId), customer.Cookie, new { rating = 1 });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "REVIEW_NOT_FOUND");
    }

    [Fact]
    // Khách chỉ sửa được đánh giá của chính mình. Đánh giá của khách khác không đổi.
    public async Task Patch_OnlyTouchesOwnReview()
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var other = await NewCustomerAsync();
        var orderId = await AddOrderAsync(customer.Id, productId);
        var otherReviewId = await SeedReviewAsync(productId, other.Id, orderId, 5, body: "Của người khác");
        var ownReviewId = await SeedReviewAsync(productId, customer.Id, orderId, 4, body: "Của tôi");

        var response = await SendAsync(new HttpMethod("PATCH"), MinePath(productId), customer.Cookie, new { rating = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ReviewAsync(ownReviewId)).Rating);
        Assert.Equal(5, (await ReviewAsync(otherReviewId)).Rating);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"rating":0}""")]
    [InlineData("""{"rating":6}""")]
    [InlineData("""{"rating":null}""")]
    [InlineData("""{"rating":"4"}""")]
    [InlineData("""{"body":7}""")]
    [InlineData("""{"rating":4,"color":"red"}""")]
    [InlineData("[]")]
    // Body rỗng, sai kiểu hoặc có trường lạ nhận 400 và đánh giá giữ nguyên.
    public async Task Patch_InvalidBody_Returns400AndChangesNothing(string json)
    {
        var productId = await NewProductAsync();
        var customer = await NewCustomerAsync();
        var orderId = await AddOrderAsync(customer.Id, productId);
        var reviewId = await SeedReviewAsync(productId, customer.Id, orderId, 5, body: "Giữ");

        var response = await SendRawAsync(new HttpMethod("PATCH"), MinePath(productId), customer.Cookie, json);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var row = await ReviewAsync(reviewId);
        Assert.Equal(5, row.Rating);
        Assert.Equal("Giữ", row.Body);
    }

    // ---------- Báo cáo vi phạm ----------

    [Fact]
    // Báo cáo lần đầu tăng `ReportCount` thêm 1 và tạo đúng một dòng báo cáo.
    public async Task Report_First_Returns201AndIncrementsCount()
    {
        var productId = await NewProductAsync();
        var author = await NewCustomerAsync();
        var reporter = await NewCustomerAsync();
        var orderId = await AddOrderAsync(author.Id, productId);
        var reviewId = await SeedReviewAsync(productId, author.Id, orderId, 1, body: "Vi phạm");

        var response = await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", reporter.Cookie, body: new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, (await ReviewAsync(reviewId)).ReportCount);
        Assert.Equal(1, await ReportRowsAsync(reviewId));
    }

    [Fact]
    // Báo cáo không cần body, vì lý do báo cáo chưa nằm trong phạm vi.
    public async Task Report_WithoutBody_Returns201()
    {
        var productId = await NewProductAsync();
        var author = await NewCustomerAsync();
        var reporter = await NewCustomerAsync();
        var orderId = await AddOrderAsync(author.Id, productId);
        var reviewId = await SeedReviewAsync(productId, author.Id, orderId, 1);

        var response = await _client.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", body: null, reporter.Cookie));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    // Báo cáo lần hai của cùng khách nhận 409 và `ReportCount` không đổi.
    public async Task Report_Second_Returns409AndKeepsCount()
    {
        var productId = await NewProductAsync();
        var author = await NewCustomerAsync();
        var reporter = await NewCustomerAsync();
        var orderId = await AddOrderAsync(author.Id, productId);
        var reviewId = await SeedReviewAsync(productId, author.Id, orderId, 1);
        await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", reporter.Cookie, body: new { });

        var response = await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", reporter.Cookie, body: new { });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "REVIEW_ALREADY_REPORTED");
        Assert.Equal(1, (await ReviewAsync(reviewId)).ReportCount);
        Assert.Equal(1, await ReportRowsAsync(reviewId));
    }

    [Fact]
    // Hai khách khác nhau báo cáo cùng một đánh giá: `ReportCount` là 2.
    public async Task Report_FromTwoCustomers_CountsBoth()
    {
        var productId = await NewProductAsync();
        var author = await NewCustomerAsync();
        var firstReporter = await NewCustomerAsync();
        var secondReporter = await NewCustomerAsync();
        var orderId = await AddOrderAsync(author.Id, productId);
        var reviewId = await SeedReviewAsync(productId, author.Id, orderId, 1);

        await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", firstReporter.Cookie, body: new { });
        await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", secondReporter.Cookie, body: new { });

        Assert.Equal(2, (await ReviewAsync(reviewId)).ReportCount);
        Assert.Equal(2, await ReportRowsAsync(reviewId));
    }

    [Fact]
    // Hai request đồng thời của cùng khách: cả hai qua bước kiểm tra, rồi cùng chờ ở bước ghi báo cáo.
    // Chỉ một request tạo báo cáo và tăng `ReportCount`, request kia nhận 409.
    public async Task Report_Concurrent_IncrementsOnlyOnce()
    {
        var productId = await NewProductAsync();
        var author = await NewCustomerAsync();
        var reporter = await NewCustomerAsync();
        var orderId = await AddOrderAsync(author.Id, productId);
        var reviewId = await SeedReviewAsync(productId, author.Id, orderId, 1);

        var (pending, finishedWhileLocked) = await RunWhileTableLockedAsync(LockReportsSql,
            () => SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", reporter.Cookie, body: new { }),
            () => SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", reporter.Cookie, body: new { }));

        Assert.False(finishedWhileLocked, "Request ghi phải chờ khóa bảng ở bước ghi báo cáo.");
        var statusCodes = (await pending).Select(response => (int)response.StatusCode).OrderBy(code => code).ToList();
        Assert.Equal([201, 409], statusCodes);
        Assert.Equal(1, (await ReviewAsync(reviewId)).ReportCount);
        Assert.Equal(1, await ReportRowsAsync(reviewId));
    }

    [Fact]
    // Báo cáo đánh giá của chính mình nhận 409 và không đổi dữ liệu.
    public async Task Report_OwnReview_Returns409()
    {
        var productId = await NewProductAsync();
        var author = await NewCustomerAsync();
        var orderId = await AddOrderAsync(author.Id, productId);
        var reviewId = await SeedReviewAsync(productId, author.Id, orderId, 5);

        var response = await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", author.Cookie, body: new { });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "CANNOT_REPORT_OWN_REVIEW");
        Assert.Equal(0, (await ReviewAsync(reviewId)).ReportCount);
        Assert.Equal(0, await ReportRowsAsync(reviewId));
    }

    [Fact]
    // Đánh giá đang ẩn nhận 404, như đánh giá không tồn tại, và không tăng `ReportCount`.
    public async Task Report_HiddenReview_Returns404()
    {
        var productId = await NewProductAsync();
        var author = await NewCustomerAsync();
        var reporter = await NewCustomerAsync();
        var orderId = await AddOrderAsync(author.Id, productId);
        var reviewId = await SeedReviewAsync(productId, author.Id, orderId, 1, hiddenAt: DateTime.UtcNow);

        var response = await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", reporter.Cookie, body: new { });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "REVIEW_NOT_FOUND");
        Assert.Equal(0, (await ReviewAsync(reviewId)).ReportCount);
    }

    [Fact]
    // Đánh giá không tồn tại nhận 404.
    public async Task Report_UnknownReview_Returns404()
    {
        var reporter = await NewCustomerAsync();

        var response = await SendAsync(HttpMethod.Post, $"{ReportsPath}/khong-co/reports", reporter.Cookie, body: new { });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "REVIEW_NOT_FOUND");
    }

    [Fact]
    // Đánh giá đã ẩn danh (người viết đã xóa tài khoản) vẫn báo cáo được nếu đang hiện.
    public async Task Report_AnonymizedReview_Returns201()
    {
        var productId = await NewProductAsync();
        var reporter = await NewCustomerAsync();
        var orderId = await AddOrderAsync(null, productId);
        var reviewId = await SeedReviewAsync(productId, userId: null, orderId, 1);

        var response = await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", reporter.Cookie, body: new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, (await ReviewAsync(reviewId)).ReportCount);
    }

    [Fact]
    // Chưa đăng nhập nhận 401, nhân viên nhận 403. Cả hai không tăng `ReportCount`.
    public async Task Report_WithoutSessionOrAsStaff_IsRejected()
    {
        var productId = await NewProductAsync();
        var author = await NewCustomerAsync();
        var orderId = await AddOrderAsync(author.Id, productId);
        var reviewId = await SeedReviewAsync(productId, author.Id, orderId, 1);
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(_database, "support");
        _userIds.Add(staff.Id);

        var anonymous = await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", cookie: null, body: new { });
        var asStaff = await SendAsync(HttpMethod.Post, $"{ReportsPath}/{reviewId}/reports", $"pk_session={token}", body: new { });

        await AssertErrorAsync(anonymous, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        await AssertErrorAsync(asStaff, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(0, (await ReviewAsync(reviewId)).ReportCount);
    }
}
