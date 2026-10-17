using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Contracts.Reading;
using Pickory.Modules.Commerce.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-22: Kiểm tra tích hợp cổng đọc dữ liệu ICommerceReader qua DI container thật.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CommerceReaderTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly List<string> _orderIds = [];
    private readonly List<string> _productIds = [];
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public CommerceReaderTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new CartTestKit(factory, database);
    }

    public async Task InitializeAsync()
    {
        // Khởi tạo môi trường dữ liệu giỏ hàng và danh mục cho test.
        await _kit.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var commerce = _database.CreateCommerceContext())
        {
            // Xóa đánh giá trước vì khóa ngoại chặn xóa đơn còn đánh giá liên kết.
            await commerce.Reviews.Where(review => _productIds.Contains(review.ProductId)).ExecuteDeleteAsync();
            await commerce.OrderItems.Where(item => _orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await commerce.Orders.Where(order => _orderIds.Contains(order.Id)).ExecuteDeleteAsync();
        }

        // Dọn dẹp dữ liệu sản phẩm và cấu hình dùng trong test kit.
        await _kit.CleanupAsync();
    }

    private string OrderCode(string prefix) => $"{prefix}-{_suffix}".ToUpperInvariant();

    private async Task<T> WithReaderAsync<T>(Func<ICommerceReader, Task<T>> action)
    {
        // Mở scope dịch vụ độc lập như môi trường request thật để kiểm tra đăng ký DI.
        using var scope = _factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICommerceReader>();
        return await action(reader);
    }

    private async Task<string> CreateProductAsync()
    {
        // Tạo sản phẩm thử nghiệm để gắn đơn hàng và đánh giá.
        var product = await _kit.AddProductAsync();
        _productIds.Add(product.Id);
        return product.Id;
    }

    private async Task<string> SeedOrderAsync(
        string? userId,
        string productId,
        OrderStatus status = OrderStatus.Delivered,
        PaymentStatus paymentStatus = PaymentStatus.Paid,
        PaymentMethod paymentMethod = PaymentMethod.Cod,
        string recipientName = "Nguyễn Văn An",
        DateTime? createdAt = null,
        string? code = null,
        string? anonymousId = null)
    {
        var orderId = Guid.NewGuid().ToString();
        var orderCode = code ?? ("CR" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() + "-" + _suffix);

        // Tạo dữ liệu đơn hàng với các trường bản chụp tại thời điểm đặt.
        var order = new Order
        {
            Id = orderId,
            Code = orderCode,
            UserId = userId,
            AnonymousId = anonymousId ?? ("anon-" + orderCode),
            Contact = JsonSerializer.SerializeToDocument(new
            {
                name = recipientName,
                phone = "0901234567",
                email = "reader@example.com",
            }),
            ShippingAddress = JsonSerializer.SerializeToDocument(new
            {
                provinceCode = "1000001",
                provinceName = "Hồ Chí Minh",
                wardCode = "1003646",
                wardName = "Phường Bến Nghé",
                line = "12 Đường Thử Nghiệm",
            }),
            PaymentMethod = paymentMethod,
            Status = status,
            PaymentStatus = paymentStatus,
            Flags = [],
            Subtotal = 200_000,
            ShippingFee = 30_000,
            ShippingDiscount = 0,
            Total = 230_000,
            AccessTokenHash = "hash-" + orderCode,
            IdempotencyKey = "key-" + orderCode,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        // Gắn dòng sản phẩm tương ứng vào đơn hàng.
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid().ToString(),
            OrderId = orderId,
            SkuId = "sku-" + orderCode,
            ProductId = productId,
            NameSnapshot = "Sản phẩm kiểm thử",
            SkuCodeSnapshot = "SKU-" + orderCode,
            OptionValues = JsonSerializer.SerializeToDocument(new { Size = "L" }),
            UnitPrice = 100_000,
            Quantity = 2,
        });

        await using var commerce = _database.CreateCommerceContext();
        commerce.Orders.Add(order);
        await commerce.SaveChangesAsync();

        _orderIds.Add(orderId);
        return orderId;
    }

    private async Task<List<string>> SeedOrdersBatchAsync(
        string userId, string productId, int count, DateTime baseTime)
    {
        var orders = new List<Order>(count);
        var codes = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var orderId = Guid.NewGuid().ToString();
            var orderCode = $"BTH{i:00}-{_suffix}";
            codes.Add(orderCode);

            var order = new Order
            {
                Id = orderId,
                Code = orderCode,
                UserId = userId,
                AnonymousId = "anon-" + orderCode,
                Contact = JsonSerializer.SerializeToDocument(new
                {
                    name = "Khách Hàng Batch",
                    phone = "0901234567",
                    email = "batch@example.com",
                }),
                ShippingAddress = JsonSerializer.SerializeToDocument(new
                {
                    provinceCode = "1000001",
                    provinceName = "Hồ Chí Minh",
                    wardCode = "1003646",
                    wardName = "Phường Bến Nghé",
                    line = "12 Đường Thử Nghiệm",
                }),
                PaymentMethod = PaymentMethod.Cod,
                Status = OrderStatus.Delivered,
                PaymentStatus = PaymentStatus.Paid,
                Flags = [],
                Subtotal = 100_000,
                ShippingFee = 0,
                ShippingDiscount = 0,
                Total = 100_000,
                AccessTokenHash = "hash-" + orderCode,
                IdempotencyKey = "key-" + orderCode,
                CreatedAt = baseTime.AddMinutes(i),
            };

            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderId,
                SkuId = "sku-" + orderCode,
                ProductId = productId,
                NameSnapshot = "Sản phẩm kiểm thử",
                SkuCodeSnapshot = "SKU-" + orderCode,
                OptionValues = JsonSerializer.SerializeToDocument(new { Size = "M" }),
                UnitPrice = 100_000,
                Quantity = 1,
            });

            orders.Add(order);
            _orderIds.Add(orderId);
        }

        await using var commerce = _database.CreateCommerceContext();
        commerce.Orders.AddRange(orders);
        await commerce.SaveChangesAsync();

        return codes;
    }

    private async Task<string> SeedReviewAsync(
        string productId,
        string? userId,
        string orderId,
        int rating,
        DateTime? createdAt = null,
        DateTime? hiddenAt = null,
        string? body = null,
        string? id = null)
    {
        // Tạo bản ghi đánh giá với trạng thái hiển thị do kịch bản test chỉ định.
        var review = new Review
        {
            Id = id ?? Guid.NewGuid().ToString(),
            ProductId = productId,
            UserId = userId,
            OrderId = orderId,
            Rating = rating,
            Body = body,
            HiddenAt = hiddenAt,
            HiddenReason = hiddenAt is null ? null : "Nội dung không phù hợp",
            ReportCount = 0,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = createdAt ?? DateTime.UtcNow,
        };

        await using var commerce = _database.CreateCommerceContext();
        commerce.Reviews.Add(review);
        await commerce.SaveChangesAsync();
        return review.Id;
    }

    private async Task SeedReviewsBatchAsync(
        string productId, string orderId, int count, DateTime baseTime)
    {
        var reviews = new List<Review>(count);

        for (var i = 0; i < count; i++)
        {
            reviews.Add(new Review
            {
                Id = $"rv-bth-{i:00}-{_suffix}",
                ProductId = productId,
                UserId = $"user-bth-{i:00}-{_suffix}",
                OrderId = orderId,
                Rating = 5,
                Body = "Tốt",
                HiddenAt = null,
                ReportCount = 0,
                CreatedAt = baseTime.AddMinutes(i),
                UpdatedAt = baseTime.AddMinutes(i),
            });
        }

        await using var commerce = _database.CreateCommerceContext();
        commerce.Reviews.AddRange(reviews);
        await commerce.SaveChangesAsync();
    }

    [Fact]
    // GetRatingSummaryAsync phải bỏ đánh giá đã ẩn và trả đủ khóa cho mọi id yêu cầu.
    public async Task GetRatingSummaryAsync_BoDanhGiaDaAn_VaTraDuKhoaChoSanPham()
    {
        var productA = await CreateProductAsync();
        var productB = await CreateProductAsync();
        var productC = await CreateProductAsync();

        var orderA1 = await SeedOrderAsync("user-1", productA);
        var orderA2 = await SeedOrderAsync("user-2", productA);
        var orderA3 = await SeedOrderAsync("user-3", productA);
        var orderB1 = await SeedOrderAsync("user-4", productB);

        var baseTime = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

        // Đánh giá hiển thị 5 sao cho sản phẩm A.
        await SeedReviewAsync(productA, "user-1", orderA1, 5, baseTime);
        // Đánh giá hiển thị 4 sao cho sản phẩm A.
        await SeedReviewAsync(productA, "user-2", orderA2, 4, baseTime.AddMinutes(1));
        // Đánh giá 1 sao bị ẩn của sản phẩm A không được tính vào tóm tắt.
        await SeedReviewAsync(productA, "user-3", orderA3, 1, baseTime.AddMinutes(2), hiddenAt: baseTime.AddMinutes(3));

        // Đánh giá 3 sao cho sản phẩm B.
        await SeedReviewAsync(productB, "user-4", orderB1, 3, baseTime);

        // Gọi lấy tóm tắt cho nhiều sản phẩm có kèm id trùng và khoảng trắng.
        var summaries = await WithReaderAsync(reader =>
            reader.GetRatingSummaryAsync([productA, productB, productC, productA, "  ", ""], CancellationToken.None));

        // Khẳng định kết quả có đúng 3 khóa duy nhất tương ứng 3 sản phẩm hợp lệ.
        Assert.Equal(3, summaries.Count);
        Assert.True(summaries.ContainsKey(productA));
        Assert.True(summaries.ContainsKey(productB));
        Assert.True(summaries.ContainsKey(productC));

        var summaryA = summaries[productA];
        // Bỏ đánh giá ẩn nên tổng số đánh giá của A chỉ là 2.
        Assert.Equal(2, summaryA.Count);
        // Điểm trung bình tính từ 5 và 4 là 4.5.
        Assert.Equal(4.5, summaryA.Average);
        Assert.Equal(1, summaryA.Distribution[5]);
        Assert.Equal(1, summaryA.Distribution[4]);
        Assert.Equal(0, summaryA.Distribution[3]);
        Assert.Equal(0, summaryA.Distribution[2]);
        Assert.Equal(0, summaryA.Distribution[1]);

        var summaryB = summaries[productB];
        Assert.Equal(1, summaryB.Count);
        Assert.Equal(3.0, summaryB.Average);
        Assert.Equal(1, summaryB.Distribution[3]);

        var summaryC = summaries[productC];
        // Sản phẩm chưa có đánh giá có Count bằng 0 và Average là null.
        Assert.Equal(0, summaryC.Count);
        Assert.Null(summaryC.Average);
        Assert.Equal([1, 2, 3, 4, 5], summaryC.Distribution.Keys.OrderBy(k => k));
        Assert.All(summaryC.Distribution.Values, count => Assert.Equal(0, count));

        // Danh sách rỗng phải trả về từ điển rỗng mà không lỗi.
        var emptyResult = await WithReaderAsync(reader =>
            reader.GetRatingSummaryAsync([], CancellationToken.None));
        Assert.Empty(emptyResult);
    }

    [Fact]
    // GetVisibleReviewsAsync bỏ đánh giá ẩn và phân trang con trỏ không lặp không sót dòng.
    public async Task GetVisibleReviewsAsync_BoDanhGiaDaAn_PhanTrangConTroOnDinh()
    {
        var productId = await CreateProductAsync();
        var baseTime = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

        var order1 = await SeedOrderAsync("user-1", productId, recipientName: "Lê Văn Một");
        var order2 = await SeedOrderAsync("user-2", productId, recipientName: "Trần Thị Hai");
        var order3 = await SeedOrderAsync("user-3", productId, recipientName: "Hoàng Văn Ba");
        var order4 = await SeedOrderAsync("user-4", productId, recipientName: "Phạm Thị Bốn");
        var order5 = await SeedOrderAsync("user-5", productId, recipientName: "Vũ Văn Năm");

        // Dòng mới nhất đánh giá 5 sao.
        await SeedReviewAsync(productId, "user-1", order1, 5, baseTime.AddMinutes(4), id: $"rev-01-{_suffix}");
        // Dòng bị ẩn đánh giá 4 sao phải bị bỏ qua trong danh sách hiển thị.
        await SeedReviewAsync(productId, "user-2", order2, 4, baseTime.AddMinutes(3), hiddenAt: baseTime.AddMinutes(5), id: $"rev-02-{_suffix}");
        // Hai dòng cùng thời điểm tạo để kiểm tra sắp xếp Id ổn định.
        await SeedReviewAsync(productId, "user-3", order3, 3, baseTime.AddMinutes(2), id: $"rev-03-{_suffix}");
        await SeedReviewAsync(productId, "user-4", order4, 2, baseTime.AddMinutes(2), id: $"rev-04-{_suffix}");
        // Dòng cũ nhất đánh giá 1 sao.
        await SeedReviewAsync(productId, "user-5", order5, 1, baseTime.AddMinutes(1), id: $"rev-05-{_suffix}");

        // Đọc trang đầu với kích thước 2 bản ghi.
        var page1 = await WithReaderAsync(reader =>
            reader.GetVisibleReviewsAsync(productId, 2, null, CancellationToken.None));

        Assert.Equal(2, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);
        Assert.Equal($"rev-01-{_suffix}", page1.Items[0].Id);
        // rev-02 bị ẩn nên dòng tiếp theo của trang 1 là rev-03.
        Assert.Equal($"rev-03-{_suffix}", page1.Items[1].Id);

        // Đọc trang tiếp theo bằng NextCursor từ trang trước.
        var page2 = await WithReaderAsync(reader =>
            reader.GetVisibleReviewsAsync(productId, 2, page1.NextCursor, CancellationToken.None));

        Assert.Equal(2, page2.Items.Count);
        Assert.Equal($"rev-04-{_suffix}", page2.Items[0].Id);
        Assert.Equal($"rev-05-{_suffix}", page2.Items[1].Id);
        // Đã hết dữ liệu nên con trỏ tiếp theo phải là null.
        Assert.Null(page2.NextCursor);

        // Kiểm tra định dạng tên người viết không lộ đầy đủ họ tên.
        Assert.Equal("Một L.", page1.Items[0].AuthorName);
        Assert.Equal(DateTimeKind.Utc, page1.Items[0].CreatedAt.Kind);
    }

    [Fact]
    // GetVisibleReviewsAsync với limit bằng 0 bị kéo về 1 và trả về con trỏ tiếp theo.
    public async Task GetVisibleReviewsAsync_LimitBangKhong_KeoVeMotVaCoNextCursor()
    {
        var productId = await CreateProductAsync();
        var orderId = await SeedOrderAsync("user-lim0", productId);
        var baseTime = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);

        // Tạo 3 đánh giá hiển thị cho sản phẩm.
        await SeedReviewAsync(productId, "user-lim0-1", orderId, 5, baseTime.AddMinutes(2));
        await SeedReviewAsync(productId, "user-lim0-2", orderId, 4, baseTime.AddMinutes(1));
        await SeedReviewAsync(productId, "user-lim0-3", orderId, 3, baseTime);

        // Gọi với limit bằng 0 phải được kéo về 1 bản ghi.
        var page = await WithReaderAsync(reader =>
            reader.GetVisibleReviewsAsync(productId, 0, null, CancellationToken.None));

        Assert.Single(page.Items);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    // GetVisibleReviewsAsync với 51 đánh giá và limit 100 bị kéo về trần 50 đánh giá.
    public async Task GetVisibleReviewsAsync_NamMuoiMotDanhGia_LimitMotTram_KeoVeNamMuoi()
    {
        var productId = await CreateProductAsync();
        var orderId = await SeedOrderAsync(null, productId);
        var baseTime = new DateTime(2026, 6, 3, 7, 0, 0, DateTimeKind.Utc);

        // Tạo 51 đánh giá hiển thị trên cùng một đơn hàng cho sản phẩm.
        await SeedReviewsBatchAsync(productId, orderId, 51, baseTime);

        // Gọi với limit 100 phải bị kéo về trần 50 bản ghi.
        var page = await WithReaderAsync(reader =>
            reader.GetVisibleReviewsAsync(productId, 100, null, CancellationToken.None));

        // Khẳng định chỉ trả về đúng 50 đánh giá mới nhất.
        Assert.Equal(50, page.Items.Count);
        Assert.NotNull(page.NextCursor);
        // Đánh giá mới nhất nằm ở đầu danh sách.
        Assert.Equal($"rv-bth-50-{_suffix}", page.Items[0].Id);
        // Đánh giá cũ nhất bị loại khỏi trang đầu tiên.
        Assert.DoesNotContain(page.Items, r => r.Id == $"rv-bth-00-{_suffix}");
    }

    [Fact]
    // Cursor không hợp lệ phải ném ArgumentException với đúng paramName.
    public async Task GetVisibleReviewsAsync_CursorKhongHopLe_NemArgumentException()
    {
        var productId = await CreateProductAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            WithReaderAsync(reader =>
                reader.GetVisibleReviewsAsync(productId, 10, "chuoi-cursor-sai-dinh-dang", CancellationToken.None)));

        Assert.Equal("cursor", ex.ParamName);
    }

    [Fact]
    // Sản phẩm không tồn tại trả về trang rỗng và không gọi ICatalogReader.
    public async Task GetVisibleReviewsAsync_SanPhamKhongTonTai_TraTrangRong()
    {
        var page = await WithReaderAsync(reader =>
            reader.GetVisibleReviewsAsync("product-chua-tung-co", 10, null, CancellationToken.None));

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    // Đánh giá đã ẩn danh có UserId bằng null trả về tên người viết là nhãn chung.
    public async Task GetVisibleReviewsAsync_DanhGiaAnDanh_TraVeNhanChung()
    {
        var productId = await CreateProductAsync();
        var orderId = await SeedOrderAsync(null, productId, recipientName: "Ngô Văn Khách");

        // Tạo đánh giá ẩn danh với UserId là null.
        await SeedReviewAsync(productId, null, orderId, 5);

        var page = await WithReaderAsync(reader =>
            reader.GetVisibleReviewsAsync(productId, 10, null, CancellationToken.None));

        Assert.Single(page.Items);
        // Đánh giá đã ẩn danh phải hiện tên là Khách hàng.
        Assert.Equal("Khách hàng", page.Items[0].AuthorName);
    }

    [Fact]
    // GetPurchasedProductIdsAsync chỉ tính đơn hợp lệ và không trùng lặp mã sản phẩm.
    public async Task GetPurchasedProductIdsAsync_ChiTinhDonHopLe_VaKhongTrungLap()
    {
        var product1 = await CreateProductAsync();
        var product2 = await CreateProductAsync();
        var product3 = await CreateProductAsync();
        var product4 = await CreateProductAsync();
        var product5 = await CreateProductAsync();
        var product6 = await CreateProductAsync();

        var userId = "customer-purchased-" + Guid.NewGuid().ToString("N")[..8];
        var otherUserId = "customer-other-" + Guid.NewGuid().ToString("N")[..8];

        // Đơn đã thanh toán với sản phẩm 1 được tính.
        await SeedOrderAsync(userId, product1, OrderStatus.Confirmed, PaymentStatus.Paid);
        // Đơn giao hàng COD chưa thanh toán với sản phẩm 2 được tính.
        await SeedOrderAsync(userId, product2, OrderStatus.Delivered, PaymentStatus.Unpaid);
        // Đơn thứ hai chứa sản phẩm 1 để kiểm tra tính duy nhất.
        await SeedOrderAsync(userId, product1, OrderStatus.Delivered, PaymentStatus.Paid);

        // Đơn chưa thanh toán với sản phẩm 3 không được tính.
        await SeedOrderAsync(userId, product3, OrderStatus.PendingPayment, PaymentStatus.Unpaid);
        // Đơn đã hủy với sản phẩm 4 không được tính.
        await SeedOrderAsync(userId, product4, OrderStatus.Cancelled, PaymentStatus.Paid);
        // Đơn đã hoàn trả với sản phẩm 5 không được tính.
        await SeedOrderAsync(userId, product5, OrderStatus.Returned, PaymentStatus.Paid);
        // Đơn của người dùng khác không được tính vào kết quả.
        await SeedOrderAsync(otherUserId, product6, OrderStatus.Delivered, PaymentStatus.Paid);

        var purchasedIds = await WithReaderAsync(reader =>
            reader.GetPurchasedProductIdsAsync(userId, CancellationToken.None));

        // Khẳng định kết quả chỉ chứa sản phẩm 1 và 2 không trùng lặp.
        var expected = new[] { product1, product2 }.OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, purchasedIds);

        // Kiểm tra userId rỗng hoặc khoảng trắng trả về danh sách rỗng.
        var emptyResult = await WithReaderAsync(reader =>
            reader.GetPurchasedProductIdsAsync("   ", CancellationToken.None));
        Assert.Empty(emptyResult);
    }

    [Fact]
    // GetOrdersForCustomerAsync trả đúng số lượng đơn mới nhất và kéo giới hạn về khoảng an toàn.
    public async Task GetOrdersForCustomerAsync_TraDungSoLuongDon_VaKeoGioiHanVeKhoang()
    {
        var productId = await CreateProductAsync();
        var userId = "customer-orders-" + Guid.NewGuid().ToString("N")[..8];
        var otherUserId = "other-orders-" + Guid.NewGuid().ToString("N")[..8];
        var baseTime = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

        // Tạo 4 đơn hàng cho khách với thời điểm tạo xác định giảm dần.
        await SeedOrderAsync(userId, productId, OrderStatus.Delivered, PaymentStatus.Paid, createdAt: baseTime.AddHours(4), code: OrderCode("ORD-04"));
        await SeedOrderAsync(userId, productId, OrderStatus.Shipping, PaymentStatus.Paid, createdAt: baseTime.AddHours(3), code: OrderCode("ORD-03"));
        await SeedOrderAsync(userId, productId, OrderStatus.Confirmed, PaymentStatus.Paid, createdAt: baseTime.AddHours(2), code: OrderCode("ORD-02"));
        // Đơn đã hủy vẫn nằm trong lịch sử đơn hàng của khách.
        await SeedOrderAsync(userId, productId, OrderStatus.Cancelled, PaymentStatus.Unpaid, createdAt: baseTime.AddHours(1), code: OrderCode("ORD-01"));

        // Đơn của người khác không được lẫn vào kết quả.
        await SeedOrderAsync(otherUserId, productId, OrderStatus.Delivered, PaymentStatus.Paid, createdAt: baseTime.AddHours(5));

        // Yêu cầu lấy 2 đơn mới nhất.
        var orders = await WithReaderAsync(reader =>
            reader.GetOrdersForCustomerAsync(userId, 2, CancellationToken.None));

        Assert.Equal(2, orders.Count);
        Assert.Equal(OrderCode("ORD-04"), orders[0].Code);
        Assert.Equal(OrderCode("ORD-03"), orders[1].Code);
        Assert.Equal(2, orders[0].ItemCount);
        Assert.Equal(DateTimeKind.Utc, orders[0].CreatedAt.Kind);

        // Giới hạn bằng 0 hoặc âm bị kéo về 1 đơn.
        var clampedMin = await WithReaderAsync(reader =>
            reader.GetOrdersForCustomerAsync(userId, 0, CancellationToken.None));
        Assert.Single(clampedMin);

        // Giới hạn vượt quá 50 bị kéo về 50 và trả đủ 4 đơn hiện có.
        var clampedMax = await WithReaderAsync(reader =>
            reader.GetOrdersForCustomerAsync(userId, 100, CancellationToken.None));
        Assert.Equal(4, clampedMax.Count);

        // Mã khách hàng rỗng trả về danh sách rỗng.
        var emptyOrders = await WithReaderAsync(reader =>
            reader.GetOrdersForCustomerAsync("", 10, CancellationToken.None));
        Assert.Empty(emptyOrders);
    }

    [Fact]
    // GetOrdersForCustomerAsync giới hạn tối đa 50 đơn khi khách có 51 đơn và yêu cầu limit 100.
    public async Task GetOrdersForCustomerAsync_NamMuoiMotDon_LimitMotTram_KeoVeNamMuoiDon()
    {
        var productId = await CreateProductAsync();
        var userId = "customer-51orders-" + Guid.NewGuid().ToString("N")[..8];
        var baseTime = new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc);

        // Tạo 51 đơn hàng theo thứ tự thời gian tăng dần từ index 0 đến 50 trong một lệnh lưu.
        var codes = await SeedOrdersBatchAsync(userId, productId, 51, baseTime);

        // Yêu cầu limit 100 phải bị kéo về trần 50 đơn.
        var orders = await WithReaderAsync(reader =>
            reader.GetOrdersForCustomerAsync(userId, 100, CancellationToken.None));

        // Khẳng định chỉ trả về đúng 50 đơn hàng mới nhất.
        Assert.Equal(50, orders.Count);
        // Đơn đầu tiên trong kết quả là đơn mới nhất (index 50).
        Assert.Equal(codes[50], orders[0].Code);
        // Đơn cuối cùng trong kết quả là đơn ở index 1.
        Assert.Equal(codes[1], orders[49].Code);
        // Đơn cũ nhất ở index 0 phải bị loại trừ khỏi kết quả.
        Assert.DoesNotContain(orders, o => o.Code == codes[0]);
    }

    [Fact]
    // GetOrderByCodeAsync tìm thấy đơn khi mã có khoảng trắng hoặc viết thường.
    public async Task GetOrderByCodeAsync_TraDungChiTietDonHang_VaChuanHoaMa()
    {
        var productId = await CreateProductAsync();
        var userId = "detail-user-" + Guid.NewGuid().ToString("N")[..8];
        var code = OrderCode("ORD-DETAIL-99");

        await SeedOrderAsync(userId, productId, OrderStatus.Confirmed, PaymentStatus.Paid, code: code);

        // Tra cứu với mã viết thường kèm khoảng trắng hai đầu.
        var detail = await WithReaderAsync(reader =>
            reader.GetOrderByCodeAsync($"  {code.ToLowerInvariant()}  ", CancellationToken.None));

        Assert.NotNull(detail);
        Assert.Equal(code, detail.Code);
        Assert.Equal(userId, detail.UserId);
        Assert.StartsWith("anon-", detail.AnonymousId);
        Assert.Equal(OrderStatus.Confirmed, detail.Status);
        Assert.Equal(PaymentStatus.Paid, detail.PaymentStatus);
        Assert.Equal(PaymentMethod.Cod, detail.PaymentMethod);
        Assert.Equal(200_000, detail.Subtotal);
        Assert.Equal(30_000, detail.ShippingFee);
        Assert.Equal(0, detail.ShippingDiscount);
        Assert.Equal(230_000, detail.Total);
        Assert.Equal(DateTimeKind.Utc, detail.CreatedAt.Kind);

        Assert.Single(detail.Items);
        var line = detail.Items[0];
        Assert.Equal(productId, line.ProductId);
        Assert.Equal("Sản phẩm kiểm thử", line.ProductName);
        Assert.Equal("L", line.VariantName);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(100_000, line.UnitPrice);
        Assert.Equal(200_000, line.LineTotal);

        // Mã không tồn tại trả về null.
        var notFound = await WithReaderAsync(reader =>
            reader.GetOrderByCodeAsync("ORD-KHONG-CO", CancellationToken.None));
        Assert.Null(notFound);

        // Mã rỗng trả về null không gọi database.
        var emptyCode = await WithReaderAsync(reader =>
            reader.GetOrderByCodeAsync("   ", CancellationToken.None));
        Assert.Null(emptyCode);
    }

    [Fact]
    // GetOrdersForAnonymousAsync chỉ trả đơn khớp chính xác mã ẩn danh.
    public async Task GetOrdersForAnonymousAsync_ChiTraDonKhopMaAnDanh()
    {
        var productId = await CreateProductAsync();
        var targetAnonId = "anon-target-" + Guid.NewGuid().ToString("N")[..8];
        var otherAnonId = "anon-other-" + Guid.NewGuid().ToString("N")[..8];

        // Tạo đơn hàng của mã ẩn danh mục tiêu.
        await SeedOrderAsync(null, productId, anonymousId: targetAnonId, code: OrderCode("ANON-01"));
        // Tạo đơn hàng của mã ẩn danh khác.
        await SeedOrderAsync(null, productId, anonymousId: otherAnonId, code: OrderCode("ANON-02"));
        // Tạo đơn hàng có mã ẩn danh chuỗi rỗng trong database.
        await SeedOrderAsync(null, productId, anonymousId: string.Empty, code: OrderCode("ANON-03"));

        var orders = await WithReaderAsync(reader =>
            reader.GetOrdersForAnonymousAsync(targetAnonId, 10, CancellationToken.None));

        Assert.Single(orders);
        Assert.Equal(OrderCode("ANON-01"), orders[0].Code);

        // Mã ẩn danh rỗng và khoảng trắng đều phải trả về danh sách rỗng để không khớp đơn chuỗi rỗng.
        var emptyOrders = await WithReaderAsync(reader =>
            reader.GetOrdersForAnonymousAsync(string.Empty, 10, CancellationToken.None));
        Assert.Empty(emptyOrders);

        var whitespaceOrders = await WithReaderAsync(reader =>
            reader.GetOrdersForAnonymousAsync("   ", 10, CancellationToken.None));
        Assert.Empty(whitespaceOrders);
    }
}
