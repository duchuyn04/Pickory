using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-20: hàng chờ kiểm duyệt và ẩn, hiện đánh giá. Test đi qua route thật và đọc lại DB, audit và sự kiện.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminReviewModerationTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string AdminReviewsPath = "/api/admin/reviews";
    private const string AuditTargetType = "review";
    private const string LockReviewsSql = "LOCK TABLE reviews IN SHARE ROW EXCLUSIVE MODE";

    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _productIds = [];
    private readonly List<string> _orderIds = [];
    private readonly List<string> _reviewIds = [];

    public AdminReviewModerationTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
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
        await using (var identity = _database.CreateIdentityContext())
        {
            await identity.AuditLogs
                .Where(log => log.TargetType == AuditTargetType && _reviewIds.Contains(log.TargetId))
                .ExecuteDeleteAsync();
        }

        await using (var commerce = _database.CreateCommerceContext())
        {
            // Xóa đánh giá trước đơn vì khóa ngoại chặn xóa đơn còn đánh giá. Báo cáo xóa theo đánh giá.
            await commerce.Reviews.Where(review => _reviewIds.Contains(review.Id)).ExecuteDeleteAsync();
            await commerce.OrderItems.Where(item => _orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await commerce.Orders.Where(order => _orderIds.Contains(order.Id)).ExecuteDeleteAsync();
        }

        await _kit.CleanupAsync();
    }

    private static string DetailPath(string reviewId) => $"{AdminReviewsPath}/{reviewId}";

    private static string PublicPath(string productId) => $"/api/products/{productId}/reviews";

    private async Task<string> StaffSessionAsync(string roleCode)
    {
        var (_, session) = await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com20");
        return $"pk_session={session}";
    }

    private async Task<(string UserId, string Cookie)> StaffAsync(string roleCode = DefaultRoles.SupportCode)
    {
        var (user, session) = await IdentityTestData.CreateStaffSessionAsync(_database, roleCode, "com20");
        return (user.Id, $"pk_session={session}");
    }

    private async Task<string> NewProductAsync()
    {
        var product = await _kit.AddProductAsync();
        _productIds.Add(product.Id);
        return product.Id;
    }

    // Ghi thẳng một đơn đã giao mang tên người nhận, vì đánh giá lấy tên người viết từ đơn này.
    private async Task<string> AddOrderAsync(string productId, string recipientName = "Nguyễn Văn An")
    {
        var orderId = Guid.NewGuid().ToString();
        var code = "MD" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var order = new Order
        {
            Id = orderId,
            Code = code,
            UserId = null,
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
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Paid,
            Flags = [],
            Subtotal = 200_000,
            ShippingFee = 30_000,
            Total = 230_000,
            AccessTokenHash = "hash-" + code,
            IdempotencyKey = "key-" + code,
            CreatedAt = DateTime.UtcNow,
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

    // Ghi thẳng một đánh giá để dựng hàng chờ. Thời điểm tạo, số báo cáo và trạng thái ẩn do test chọn.
    private async Task<string> SeedReviewAsync(
        string productId, string orderId, int rating = 5, int reportCount = 0, DateTime? createdAt = null,
        DateTime? hiddenAt = null, string? hiddenReason = null, bool anonymous = false, string? body = "Nội dung")
    {
        var review = new Review
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = productId,
            // Mỗi đánh giá có người viết riêng, vì khóa duy nhất `(ProductId, UserId)` chỉ cho một đánh giá mỗi người.
            UserId = anonymous ? null : "user-" + Guid.NewGuid().ToString("N"),
            OrderId = orderId,
            Rating = rating,
            Body = body,
            HiddenAt = hiddenAt,
            HiddenReason = hiddenReason,
            ReportCount = reportCount,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = createdAt ?? DateTime.UtcNow,
        };
        await using var commerce = _database.CreateCommerceContext();
        commerce.Reviews.Add(review);
        await commerce.SaveChangesAsync();
        _reviewIds.Add(review.Id);
        return review.Id;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, object? body = null)
    {
        return await _client.SendAsync(body is null && method == HttpMethod.Get
            ? ActorHttp.CreateRequest(method, path, cookie)
            : ActorHttp.CreateJsonRequest(method, path, body, cookie));
    }

    private Task<HttpResponseMessage> ModerateAsync(string reviewId, string cookie, bool hidden, string? reason) =>
        reason is null
            ? SendAsync(HttpMethod.Patch, DetailPath(reviewId), cookie, new { hidden })
            : SendAsync(HttpMethod.Patch, DetailPath(reviewId), cookie, new { hidden, reason });

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

    private async Task<Review> ReviewAsync(string reviewId)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Reviews.AsNoTracking().SingleAsync(review => review.Id == reviewId);
    }

    private async Task<List<AuditLog>> AuditOfAsync(string reviewId)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.AuditLogs.AsNoTracking()
            .Where(log => log.TargetType == AuditTargetType && log.TargetId == reviewId)
            .ToListAsync();
    }

    private int EventCount(string reviewId, ReviewChange change) =>
        _factory.DomainEvents.OfType<ReviewChangedEvent>().Count(e => e.ReviewId == reviewId && e.Change == change);

    private int EventCount(string reviewId) =>
        _factory.DomainEvents.OfType<ReviewChangedEvent>().Count(e => e.ReviewId == reviewId);

    // Đọc toàn bộ hàng chờ theo trang nhỏ. Test thứ tự và phân trang không phụ thuộc dữ liệu của test khác.
    private async Task<(List<JsonElement> Rows, int Total)> ReadWholeQueueAsync(string cookie, string? status = null, int limit = 100)
    {
        var rows = new List<JsonElement>();
        var total = 0;
        for (var page = 1; page <= 200; page++)
        {
            var path = $"{AdminReviewsPath}?page={page}&limit={limit}" + (status is null ? string.Empty : $"&status={status}");
            var response = await SendAsync(HttpMethod.Get, path, cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = await ReadDataAsync(response);
            total = data.GetProperty("total").GetInt32();
            var items = data.GetProperty("items").EnumerateArray().ToList();
            rows.AddRange(items);
            if (rows.Count >= total || items.Count == 0)
            {
                break;
            }
        }

        return (rows, total);
    }

    private async Task<string> ProductNameAsync(string productId)
    {
        using var scope = _factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogReader>();
        var product = await catalog.GetProductAsync(productId, CancellationToken.None);
        return product!.Name;
    }

    // Giữ khóa bảng ở chế độ chặn ghi nhưng không chặn đọc và không chặn `SELECT FOR UPDATE`.
    // Hai request cùng khóa dòng, request thắng chờ ở bước ghi, và request thua chờ khóa dòng.
    private async Task<Task<HttpResponseMessage[]>> RunWhileTableLockedAsync(
        string lockSql, params Func<Task<HttpResponseMessage>>[] requests)
    {
        await using var holder = _database.CreateCommerceContext();
        await using var holderTransaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync(lockSql);

        var pending = Task.WhenAll(requests.Select(request => request()));
        var finishedWhileLocked = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2))) == pending;
        Assert.False(finishedWhileLocked, "Request ghi phải chờ khóa bảng ở bước ghi.");

        await holderTransaction.CommitAsync();
        await pending;
        return pending;
    }

    // ---------- Hàng chờ ----------

    [Fact]
    // Đánh giá từ 3 báo cáo đứng trước mọi đánh giá khác. Nhóm này sắp theo số báo cáo giảm dần,
    // và phần còn lại sắp mới nhất trước.
    public async Task Queue_PriorityGroupFirst_ThenReportCountDescending_ThenNewest()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var now = DateTime.UtcNow;
        var threeOlder = await SeedReviewAsync(productId, orderId, reportCount: 3, createdAt: now.AddDays(-10));
        var twoNewer = await SeedReviewAsync(productId, orderId, reportCount: 2, createdAt: now.AddDays(-1));
        var fiveOldest = await SeedReviewAsync(productId, orderId, reportCount: 5, createdAt: now.AddDays(-20));
        var zeroNewest = await SeedReviewAsync(productId, orderId, reportCount: 0, createdAt: now);
        var (_, cookie) = await StaffAsync();

        var (rows, _) = await ReadWholeQueueAsync(cookie);

        var mine = new[] { threeOlder, twoNewer, fiveOldest, zeroNewest };
        var order = rows.Select(row => row.GetProperty("id").GetString()!).Where(mine.Contains).ToList();
        Assert.Equal([fiveOldest, threeOlder, zeroNewest, twoNewer], order);
    }

    [Fact]
    // Hai đánh giá cùng số báo cáo và cùng thời điểm được sắp ổn định theo `Id` tăng dần.
    public async Task Queue_SameReportCountAndTime_OrdersByIdAscending()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var moment = DateTime.UtcNow.AddDays(-2);
        var first = await SeedReviewAsync(productId, orderId, reportCount: 4, createdAt: moment);
        var second = await SeedReviewAsync(productId, orderId, reportCount: 4, createdAt: moment);
        var (_, cookie) = await StaffAsync();

        var (rows, _) = await ReadWholeQueueAsync(cookie);

        var order = rows.Select(row => row.GetProperty("id").GetString()!).Where(id => id == first || id == second).ToList();
        Assert.Equal(new[] { first, second }.OrderBy(id => id, StringComparer.Ordinal).ToList(), order);
    }

    [Fact]
    // Đánh giá có 2 báo cáo chưa vào nhóm ưu tiên, dù nhiều hơn đánh giá không có báo cáo nào.
    public async Task Queue_TwoReports_IsNotPriority()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var now = DateTime.UtcNow;
        var twoReportsOlder = await SeedReviewAsync(productId, orderId, reportCount: 2, createdAt: now.AddDays(-5));
        var noReportsNewer = await SeedReviewAsync(productId, orderId, reportCount: 0, createdAt: now);
        var (_, cookie) = await StaffAsync();

        var (rows, _) = await ReadWholeQueueAsync(cookie);

        var order = rows.Select(row => row.GetProperty("id").GetString()!)
            .Where(id => id == twoReportsOlder || id == noReportsNewer).ToList();
        Assert.Equal([noReportsNewer, twoReportsOlder], order);
    }

    [Fact]
    // Bộ lọc `status` tách đánh giá đang hiện và đang ẩn. Không truyền thì có cả hai.
    public async Task Queue_StatusFilter_SeparatesVisibleAndHidden()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var visible = await SeedReviewAsync(productId, orderId);
        var hidden = await SeedReviewAsync(productId, orderId, hiddenAt: DateTime.UtcNow, hiddenReason: "spam");
        var (_, cookie) = await StaffAsync();

        var (visibleRows, _) = await ReadWholeQueueAsync(cookie, status: "visible");
        var (hiddenRows, _) = await ReadWholeQueueAsync(cookie, status: "hidden");
        var (allRows, _) = await ReadWholeQueueAsync(cookie);

        static List<string> Ids(IEnumerable<JsonElement> rows) => rows.Select(row => row.GetProperty("id").GetString()!).ToList();
        Assert.Contains(visible, Ids(visibleRows));
        Assert.DoesNotContain(hidden, Ids(visibleRows));
        Assert.Contains(hidden, Ids(hiddenRows));
        Assert.DoesNotContain(visible, Ids(hiddenRows));
        Assert.Contains(visible, Ids(allRows));
        Assert.Contains(hidden, Ids(allRows));
        Assert.All(hiddenRows, row => Assert.True(row.GetProperty("hidden").GetBoolean()));
        Assert.All(visibleRows, row => Assert.False(row.GetProperty("hidden").GetBoolean()));
    }

    [Theory]
    [InlineData("status=archived", "status")]
    [InlineData("status=VISIBLE", "status")]
    [InlineData("status=visible&status=hidden", "status")]
    [InlineData("page=0", "page")]
    [InlineData("limit=101", "limit")]
    // Tham số sai trả 400 đúng trường, trước khi chạm DB.
    public async Task Queue_InvalidQuery_Returns400ForTheField(string query, string field)
    {
        var (_, cookie) = await StaffAsync();

        var response = await SendAsync(HttpMethod.Get, $"{AdminReviewsPath}?{query}", cookie);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", field);
    }

    [Fact]
    // Phân trang: `total` đếm theo điều kiện lọc, và hai trang liên tiếp không lặp dòng.
    public async Task Queue_Pages_DoNotRepeatRows_AndTotalMatches()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var baseTime = DateTime.UtcNow.AddDays(-30);
        var seeded = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            seeded.Add(await SeedReviewAsync(productId, orderId, createdAt: baseTime.AddMinutes(index)));
        }

        var (_, cookie) = await StaffAsync();

        var (rows, total) = await ReadWholeQueueAsync(cookie, limit: 2);

        var ids = rows.Select(row => row.GetProperty("id").GetString()!).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(total, ids.Count);
        Assert.All(seeded, id => Assert.Contains(id, ids));

        var secondPage = await SendAsync(HttpMethod.Get, $"{AdminReviewsPath}?page=2&limit=2", cookie);
        var data = await ReadDataAsync(secondPage);
        Assert.Equal(2, data.GetProperty("page").GetInt32());
        Assert.Equal(2, data.GetProperty("limit").GetInt32());
    }

    [Fact]
    // Dòng hàng chờ chỉ có đúng các trường đã chốt. Không lộ `userId`, email, số điện thoại hay `orderId`.
    public async Task Queue_Row_HasNoPrivateFields()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId, recipientName: "Nguyễn Văn An");
        var reviewId = await SeedReviewAsync(productId, orderId, reportCount: 1, body: "Rất tốt");
        var (_, cookie) = await StaffAsync();

        var (rows, _) = await ReadWholeQueueAsync(cookie);

        var row = rows.Single(item => item.GetProperty("id").GetString() == reviewId);
        var names = row.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var expected = new[]
        {
            "id", "productId", "productName", "rating", "body", "authorName", "reportCount", "hidden",
            "hiddenReason", "hiddenAt", "createdAt", "updatedAt",
        }.OrderBy(name => name, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, names);
        Assert.Equal("An N.", row.GetProperty("authorName").GetString());
        var raw = row.GetRawText();
        Assert.DoesNotContain("0901234567", raw);
        Assert.DoesNotContain("an@example.com", raw);
        Assert.DoesNotContain(orderId, raw);
        Assert.DoesNotContain("userId", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    // `productName` lấy từ catalog. Sản phẩm không còn thì `productName` là `null`.
    public async Task Queue_ProductName_ComesFromCatalogAndIsNullWhenProductIsGone()
    {
        var productId = await NewProductAsync();
        var goneProductId = "gone-" + Guid.NewGuid().ToString("N");
        var orderId = await AddOrderAsync(productId);
        var present = await SeedReviewAsync(productId, orderId);
        var gone = await SeedReviewAsync(goneProductId, orderId);
        var expectedName = await ProductNameAsync(productId);
        var (_, cookie) = await StaffAsync();

        var (rows, _) = await ReadWholeQueueAsync(cookie);

        var presentRow = rows.Single(item => item.GetProperty("id").GetString() == present);
        var goneRow = rows.Single(item => item.GetProperty("id").GetString() == gone);
        Assert.Equal(expectedName, presentRow.GetProperty("productName").GetString());
        Assert.Equal(JsonValueKind.Null, goneRow.GetProperty("productName").ValueKind);
    }

    [Fact]
    // Đánh giá đã ẩn danh hiện nhãn chung, dù đơn gắn với đánh giá vẫn giữ tên người nhận.
    public async Task Queue_AnonymizedReview_ShowsGenericName()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId, recipientName: "Trần Thị Bình");
        var reviewId = await SeedReviewAsync(productId, orderId, anonymous: true);
        var (_, cookie) = await StaffAsync();

        var (rows, _) = await ReadWholeQueueAsync(cookie);

        var row = rows.Single(item => item.GetProperty("id").GetString() == reviewId);
        Assert.Equal("Khách hàng", row.GetProperty("authorName").GetString());
    }

    [Fact]
    // Danh sách có nội dung do khách viết và tên người viết, nên không cho lưu bản sao.
    public async Task Queue_ResponseIsNoStore()
    {
        var (_, cookie) = await StaffAsync();

        var response = await SendAsync(HttpMethod.Get, AdminReviewsPath, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    // ---------- Ẩn và hiện ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    // Thiếu lý do trả 400 cho cả hai hành động. Dữ liệu, audit và sự kiện không đổi.
    public async Task Patch_MissingReason_Returns400AndChangesNothing(bool hidden)
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var reviewId = await SeedReviewAsync(
            productId, orderId,
            hiddenAt: hidden ? null : DateTime.UtcNow, hiddenReason: hidden ? null : "spam");
        var (_, cookie) = await StaffAsync();
        var before = await ReviewAsync(reviewId);

        var response = await ModerateAsync(reviewId, cookie, hidden, reason: null);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "reason");
        var after = await ReviewAsync(reviewId);
        Assert.Equal(before.HiddenAt, after.HiddenAt);
        Assert.Equal(before.HiddenReason, after.HiddenReason);
        Assert.Empty(await AuditOfAsync(reviewId));
        Assert.Equal(0, EventCount(reviewId));
    }

    [Theory]
    [InlineData(true, "report_rejected")]
    [InlineData(false, "spam")]
    [InlineData(true, "SPAM")]
    [InlineData(true, "other")]
    // Mã lý do của hành động khác hoặc ngoài danh sách trả 400 đúng trường `reason`.
    public async Task Patch_ReasonNotInListOfAction_Returns400(bool hidden, string reason)
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var reviewId = await SeedReviewAsync(
            productId, orderId,
            hiddenAt: hidden ? null : DateTime.UtcNow, hiddenReason: hidden ? null : "spam");
        var (_, cookie) = await StaffAsync();

        var response = await ModerateAsync(reviewId, cookie, hidden, reason);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "reason");
        Assert.Empty(await AuditOfAsync(reviewId));
        Assert.Equal(0, EventCount(reviewId));
    }

    [Fact]
    // Trường lạ và `hidden` sai kiểu trả 400, không đổi dữ liệu.
    public async Task Patch_UnknownFieldOrWrongType_Returns400()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var reviewId = await SeedReviewAsync(productId, orderId);
        var (_, cookie) = await StaffAsync();

        var unknown = await SendAsync(HttpMethod.Patch, DetailPath(reviewId), cookie, new { hidden = true, reason = "spam", note = "x" });
        var wrongType = await SendAsync(HttpMethod.Patch, DetailPath(reviewId), cookie, new { hidden = "true", reason = "spam" });

        await AssertErrorAsync(unknown, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "note");
        await AssertErrorAsync(wrongType, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "hidden");
        Assert.Null((await ReviewAsync(reviewId)).HiddenAt);
        Assert.Empty(await AuditOfAsync(reviewId));
    }

    [Fact]
    // Ẩn đặt giờ và mã lý do, trả dòng ở dạng hàng chờ, và dòng biến mất khỏi danh sách công khai cùng tóm tắt.
    public async Task Hide_RemovesFromPublicListAndRecomputesSummary()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var keep = await SeedReviewAsync(productId, orderId, rating: 5);
        var target = await SeedReviewAsync(productId, orderId, rating: 2);
        var (_, cookie) = await StaffAsync();

        var response = await ModerateAsync(target, cookie, hidden: true, "offensive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await ReadDataAsync(response);
        Assert.Equal(target, row.GetProperty("id").GetString());
        Assert.True(row.GetProperty("hidden").GetBoolean());
        Assert.Equal("offensive", row.GetProperty("hiddenReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("hiddenAt").ValueKind);

        var saved = await ReviewAsync(target);
        Assert.NotNull(saved.HiddenAt);
        Assert.Equal("offensive", saved.HiddenReason);

        var publicList = await ReadDataAsync(await SendAsync(HttpMethod.Get, PublicPath(productId), cookie: null));
        var summary = publicList.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("count").GetInt32());
        Assert.Equal(5.0, summary.GetProperty("average").GetDouble());
        var ids = publicList.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToList();
        Assert.Equal([keep], ids);
    }

    [Fact]
    // Hiện lại đưa dòng về danh sách công khai và tính lại tóm tắt. Cột lý do ẩn về `null`.
    public async Task Show_RestoresInPublicListAndClearsHiddenReason()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        await SeedReviewAsync(productId, orderId, rating: 5);
        var target = await SeedReviewAsync(
            productId, orderId, rating: 3, hiddenAt: DateTime.UtcNow.AddHours(-1), hiddenReason: "spam");
        var (_, cookie) = await StaffAsync();

        var response = await ModerateAsync(target, cookie, hidden: false, "report_rejected");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await ReadDataAsync(response);
        Assert.False(row.GetProperty("hidden").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("hiddenReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("hiddenAt").ValueKind);

        var saved = await ReviewAsync(target);
        Assert.Null(saved.HiddenAt);
        Assert.Null(saved.HiddenReason);

        var publicList = await ReadDataAsync(await SendAsync(HttpMethod.Get, PublicPath(productId), cookie: null));
        var summary = publicList.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("count").GetInt32());
        Assert.Equal(4.0, summary.GetProperty("average").GetDouble());
        Assert.Equal(2, publicList.GetProperty("items").GetArrayLength());
    }

    [Fact]
    // Hiện lại giữ nguyên `ReportCount` và các dòng báo cáo, vì chúng là lịch sử kiểm duyệt.
    public async Task Show_KeepsReportCountAndReportRows()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var target = await SeedReviewAsync(
            productId, orderId, reportCount: 2, hiddenAt: DateTime.UtcNow, hiddenReason: "spam");
        await using (var commerce = _database.CreateCommerceContext())
        {
            commerce.ReviewReports.Add(new ReviewReport { ReviewId = target, UserId = "reporter-1", CreatedAt = DateTime.UtcNow });
            commerce.ReviewReports.Add(new ReviewReport { ReviewId = target, UserId = "reporter-2", CreatedAt = DateTime.UtcNow });
            await commerce.SaveChangesAsync();
        }

        var (_, cookie) = await StaffAsync();

        var response = await ModerateAsync(target, cookie, hidden: false, "hidden_by_mistake");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await ReviewAsync(target)).ReportCount);
        await using var check = _database.CreateCommerceContext();
        Assert.Equal(2, await check.ReviewReports.CountAsync(report => report.ReviewId == target));
    }

    [Fact]
    // Mỗi lần ẩn ghi đúng một dòng audit với hành động, đích, trạng thái trước và sau. Audit không chứa nội dung đánh giá.
    public async Task Hide_WritesExactlyOneAuditRowWithBeforeAndAfter()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var reviewId = await SeedReviewAsync(productId, orderId, body: "Nội dung riêng tư");
        var (staffId, cookie) = await StaffAsync();

        await ModerateAsync(reviewId, cookie, hidden: true, "personal_info");

        var audit = Assert.Single(await AuditOfAsync(reviewId));
        Assert.Equal(staffId, audit.ActorId);
        Assert.Equal("commerce.review.hide", audit.Action);
        Assert.Equal("review", audit.TargetType);
        Assert.False(audit.Before!.RootElement.GetProperty("hidden").GetBoolean());
        Assert.Equal(JsonValueKind.Null, audit.Before.RootElement.GetProperty("hiddenReason").ValueKind);
        Assert.True(audit.After!.RootElement.GetProperty("hidden").GetBoolean());
        Assert.Equal("personal_info", audit.After.RootElement.GetProperty("hiddenReason").GetString());
        Assert.DoesNotContain("Nội dung riêng tư", audit.Before.RootElement.GetRawText() + audit.After.RootElement.GetRawText());
    }

    [Fact]
    // Mỗi lần hiện ghi đúng một dòng audit. Lý do hiện lại nằm trong `After`, và `Before` giữ lý do ẩn cũ.
    public async Task Show_WritesExactlyOneAuditRowWithReasonInAfter()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var reviewId = await SeedReviewAsync(productId, orderId, hiddenAt: DateTime.UtcNow, hiddenReason: "spam");
        var (staffId, cookie) = await StaffAsync();

        await ModerateAsync(reviewId, cookie, hidden: false, "report_rejected");

        var audit = Assert.Single(await AuditOfAsync(reviewId));
        Assert.Equal(staffId, audit.ActorId);
        Assert.Equal("commerce.review.show", audit.Action);
        Assert.True(audit.Before!.RootElement.GetProperty("hidden").GetBoolean());
        Assert.Equal("spam", audit.Before.RootElement.GetProperty("hiddenReason").GetString());
        Assert.False(audit.After!.RootElement.GetProperty("hidden").GetBoolean());
        Assert.Equal(JsonValueKind.Null, audit.After.RootElement.GetProperty("hiddenReason").ValueKind);
        Assert.Equal("report_rejected", audit.After.RootElement.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    // Đặt lại đúng trạng thái đang có trả 409. Không có audit mới, không phát sự kiện, không đổi dữ liệu.
    public async Task Patch_StateUnchanged_Returns409WithoutAuditOrEvent(bool hidden)
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var hiddenAt = DateTime.UtcNow.AddDays(-1);
        var reviewId = await SeedReviewAsync(
            productId, orderId,
            hiddenAt: hidden ? hiddenAt : null, hiddenReason: hidden ? "spam" : null);
        var (_, cookie) = await StaffAsync();
        var reason = hidden ? "offensive" : "report_rejected";

        var response = await ModerateAsync(reviewId, cookie, hidden, reason);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "REVIEW_STATE_UNCHANGED");
        var saved = await ReviewAsync(reviewId);
        Assert.Equal(hidden ? "spam" : null, saved.HiddenReason);
        Assert.Empty(await AuditOfAsync(reviewId));
        Assert.Equal(0, EventCount(reviewId));
    }

    [Fact]
    // Đánh giá không tồn tại trả 404 `REVIEW_NOT_FOUND`.
    public async Task Patch_UnknownReview_Returns404()
    {
        var (_, cookie) = await StaffAsync();

        var response = await ModerateAsync("missing-" + Guid.NewGuid().ToString("N"), cookie, hidden: true, "spam");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "REVIEW_NOT_FOUND");
    }

    [Fact]
    // Hai request ẩn đồng thời cho cùng đánh giá: một 200, một 409, đúng một dòng audit và một sự kiện.
    public async Task Hide_Concurrent_ReturnsOne200AndOne409WithSingleAudit()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var reviewId = await SeedReviewAsync(productId, orderId);
        var (_, firstCookie) = await StaffAsync();
        var (_, secondCookie) = await StaffAsync();

        var pending = await RunWhileTableLockedAsync(LockReviewsSql,
            () => ModerateAsync(reviewId, firstCookie, hidden: true, "spam"),
            () => ModerateAsync(reviewId, secondCookie, hidden: true, "offensive"));

        var responses = await pending;
        var statusCodes = responses.Select(response => (int)response.StatusCode).OrderBy(code => code).ToList();
        Assert.Equal([200, 409], statusCodes);
        var loser = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertErrorAsync(loser, HttpStatusCode.Conflict, "REVIEW_STATE_UNCHANGED");
        Assert.Single(await AuditOfAsync(reviewId));
        Assert.Equal(1, EventCount(reviewId, ReviewChange.Hidden));
    }

    [Fact]
    // Mỗi lần ẩn hoặc hiện thành công phát đúng một `ReviewChangedEvent` với loại đúng.
    public async Task Hide_ThenShow_EmitsOneEventEach()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var reviewId = await SeedReviewAsync(productId, orderId);
        var (_, cookie) = await StaffAsync();

        await ModerateAsync(reviewId, cookie, hidden: true, "spam");
        Assert.Equal(1, EventCount(reviewId, ReviewChange.Hidden));
        Assert.Equal(0, EventCount(reviewId, ReviewChange.Shown));

        await ModerateAsync(reviewId, cookie, hidden: false, "hidden_by_mistake");
        Assert.Equal(1, EventCount(reviewId, ReviewChange.Hidden));
        Assert.Equal(1, EventCount(reviewId, ReviewChange.Shown));

        var published = _factory.DomainEvents.OfType<ReviewChangedEvent>().First(e => e.ReviewId == reviewId);
        Assert.Equal(productId, published.ProductId);
        Assert.Equal(2, (await AuditOfAsync(reviewId)).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    // Ghi audit lỗi thì trạng thái quay lui. Sự kiện không phát, vì sự kiện chỉ đi sau commit.
    public async Task Patch_WhenAuditWriteFails_RollsBackAndEmitsNoEvent(bool hide)
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var reviewId = await SeedReviewAsync(
            productId, orderId,
            hiddenAt: hide ? null : DateTime.UtcNow, hiddenReason: hide ? null : "spam");
        var (_, cookie) = await StaffAsync();

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
        var eventsBefore = recorded.OfType<ReviewChangedEvent>().Count(e => e.ReviewId == reviewId);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch, DetailPath(reviewId),
            new { hidden = hide, reason = hide ? "spam" : "report_rejected" }, cookie);
        var response = await failingClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var saved = await ReviewAsync(reviewId);
        Assert.Equal(!hide, saved.HiddenAt is not null);
        Assert.Equal(hide ? null : "spam", saved.HiddenReason);
        Assert.Empty(await AuditOfAsync(reviewId));
        Assert.Equal(eventsBefore, recorded.OfType<ReviewChangedEvent>().Count(e => e.ReviewId == reviewId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    // Người nghe sự kiện đọc DB bằng kết nối riêng. Nó phải thấy trạng thái đã commit, nên sự kiện chỉ phát sau commit.
    public async Task Patch_EventHandlerSeesCommittedState(bool hide)
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var reviewId = await SeedReviewAsync(
            productId, orderId,
            hiddenAt: hide ? null : DateTime.UtcNow, hiddenReason: hide ? null : "spam");
        var (_, cookie) = await StaffAsync();
        var observation = new CommittedStateObservation();

        using var observingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(observation);
                services.AddSingleton(_database);
                services.AddSingleton<IDomainEventHandler<ReviewChangedEvent>, CommittedStateObserver>();
            });
        });
        using var observingClient = observingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch, DetailPath(reviewId),
            new { hidden = hide, reason = hide ? "spam" : "report_rejected" }, cookie);
        var response = await observingClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(hide, observation.HiddenAtWhenHandled is not null);
    }

    private sealed class CommittedStateObservation
    {
        // Giá trị `HiddenAt` mà người nghe đọc được lúc nhận sự kiện của đánh giá đang kiểm.
        public DateTime? HiddenAtWhenHandled { get; set; }
    }

    private sealed class CommittedStateObserver(
        CommittedStateObservation observation, IntegrationDatabaseFixture database)
        : IDomainEventHandler<ReviewChangedEvent>
    {
        public async Task HandleAsync(ReviewChangedEvent @event, CancellationToken cancellationToken = default)
        {
            // Kết nối mới không thấy dữ liệu chưa commit của transaction đang chạy.
            await using var commerce = database.CreateCommerceContext();
            var review = await commerce.Reviews.AsNoTracking()
                .SingleAsync(row => row.Id == @event.ReviewId, cancellationToken);
            observation.HiddenAtWhenHandled = review.HiddenAt;
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

    // ---------- Quyền ----------

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PATCH", "/dummy-id")]
    // Nhân viên bán hàng không có `reviews:moderate`, nên cả hai route trả 403.
    public async Task Sales_IsForbiddenOnBothRoutes(string method, string suffix)
    {
        var cookie = await StaffSessionAsync(DefaultRoles.SalesCode);
        object? body = method == "PATCH" ? new { hidden = true, reason = "spam" } : null;

        var response = await SendAsync(new HttpMethod(method), AdminReviewsPath + suffix, cookie, body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Nhân viên CSKH có `reviews:moderate`, nên gọi được hàng chờ.
    public async Task Support_CanReadQueue()
    {
        var cookie = await StaffSessionAsync(DefaultRoles.SupportCode);

        var response = await SendAsync(HttpMethod.Get, AdminReviewsPath, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    // Nhân viên CSKH có `reviews:moderate`, nên cũng gọi được route ẩn và hiện lại.
    public async Task Support_CanHideAndShow()
    {
        var productId = await NewProductAsync();
        var orderId = await AddOrderAsync(productId);
        var review = await SeedReviewAsync(productId, orderId, rating: 4);
        var (_, cookie) = await StaffAsync(DefaultRoles.SupportCode);

        var hidden = await ModerateAsync(review, cookie, hidden: true, "spam");
        var shown = await ModerateAsync(review, cookie, hidden: false, "hidden_by_mistake");

        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
    }

    [Fact]
    // Khách đăng nhập và khách vãng lai không vào được route kiểm duyệt.
    public async Task CustomerAndGuest_AreRejected()
    {
        var customer = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("com20c"), UserKind.Customer, DefaultRoles.CustomerCode, emailVerified: true);
        var session = await IdentityTestData.CreateSessionAsync(_database, customer.Id);

        var asCustomer = await SendAsync(HttpMethod.Get, AdminReviewsPath, $"pk_session={session}");
        var asGuest = await SendAsync(HttpMethod.Get, AdminReviewsPath, cookie: null);

        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asGuest.StatusCode);
    }
}
