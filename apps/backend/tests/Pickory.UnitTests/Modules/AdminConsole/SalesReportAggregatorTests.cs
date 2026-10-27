using Pickory.Modules.AdminConsole.Reports;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Reports;
using Xunit;

namespace Pickory.UnitTests.Modules.AdminConsole;

// Kiểm tra các hàm gộp số liệu thuần túy của báo cáo doanh thu.
public sealed class SalesReportAggregatorTests
{
    private static DailySales CreateDay(
        DateOnly day,
        long recognizedRevenue = 0,
        int paidOrderCount = 0,
        long placedOrderValue = 0,
        int createdOrderCount = 0,
        int customerCancellations = 0,
        int shopCancellations = 0,
        int paymentExpiredCancellations = 0,
        int codOrders = 0,
        int sepayOrders = 0,
        long shippingDiscount = 0,
        int soldQuantity = 0,
        OrderStatusCounts? orderStatuses = null) =>
        new(
            day,
            recognizedRevenue,
            paidOrderCount,
            placedOrderValue,
            createdOrderCount,
            new CancellationCounts(customerCancellations, shopCancellations, paymentExpiredCancellations),
            new PaymentMethodCounts(codOrders, sepayOrders),
            shippingDiscount,
            soldQuantity,
            orderStatuses ?? new OrderStatusCounts(0, 0, 0, 0, 0, 0, 0));

    private static ProductSummary CreateProductSummary(string id, string name, string? brandId = null, string? brandName = null) =>
        new(
            id,
            "slug-" + id,
            name,
            new BrandRef(brandId ?? string.Empty, brandName ?? string.Empty, "slug-" + brandId),
            100_000,
            true,
            "active",
            null);

    [Fact]
    // Khi danh sách ngày rỗng, các tổng bằng 0 còn giá trị trung bình và tỉ lệ là null.
    public void AggregateTotals_WithEmptyDays_ReturnsZeroTotalsAndNullRatios()
    {
        var totals = SalesReportAggregator.AggregateTotals([]);

        Assert.Equal(0, totals.RecognizedRevenue);
        Assert.Equal(0, totals.PlacedOrderValue);
        Assert.Equal(0, totals.PaidOrders);
        Assert.Equal(0, totals.CreatedOrders);
        Assert.Null(totals.AverageOrderValue);
        Assert.Equal(0, totals.SoldQuantity);
        Assert.Equal(0, totals.ShippingDiscount);
        Assert.Equal(0, totals.PaidByMethod.Cod);
        Assert.Equal(0, totals.PaidByMethod.Sepay);
        Assert.Equal(0, totals.Cancellations.ByCustomer);
        Assert.Equal(0, totals.Cancellations.ByShop);
        Assert.Equal(0, totals.Cancellations.PaymentExpired);
        Assert.Null(totals.PaidByMethod.CodRate);
        Assert.Null(totals.PaidByMethod.SepayRate);
        Assert.Null(totals.Cancellations.ByCustomerRate);
        Assert.Null(totals.Cancellations.ByShopRate);
        Assert.Null(totals.Cancellations.PaymentExpiredRate);
        Assert.Equal(new SalesReportOrderStatuses(0, 0, 0, 0, 0, 0, 0), totals.OrderStatuses);
    }

    [Fact]
    // Hàm cộng dồn chính xác toàn bộ số liệu của các ngày và tính đúng giá trị trung bình đơn.
    public void AggregateTotals_WithMultipleDays_CalculatesSumsAndAovCorrectly()
    {
        var day1 = CreateDay(
            new DateOnly(2026, 10, 1),
            recognizedRevenue: 1_000_000,
            paidOrderCount: 2,
            placedOrderValue: 1_200_000,
            createdOrderCount: 3,
            customerCancellations: 1,
            shopCancellations: 0,
            paymentExpiredCancellations: 0,
            codOrders: 1,
            sepayOrders: 1,
            shippingDiscount: 20_000,
            soldQuantity: 4,
            orderStatuses: new OrderStatusCounts(0, 0, 2, 0, 0, 0, 0));

        var day2 = CreateDay(
            new DateOnly(2026, 10, 2),
            recognizedRevenue: 500_000,
            paidOrderCount: 1,
            placedOrderValue: 600_000,
            createdOrderCount: 2,
            customerCancellations: 0,
            shopCancellations: 1,
            paymentExpiredCancellations: 1,
            codOrders: 0,
            sepayOrders: 2,
            shippingDiscount: 10_000,
            soldQuantity: 2,
            orderStatuses: new OrderStatusCounts(0, 0, 1, 0, 0, 0, 0));

        var totals = SalesReportAggregator.AggregateTotals([day1, day2]);

        Assert.Equal(1_500_000, totals.RecognizedRevenue);
        Assert.Equal(1_800_000, totals.PlacedOrderValue);
        Assert.Equal(3, totals.PaidOrders);
        Assert.Equal(5, totals.CreatedOrders);
        Assert.Equal(500_000, totals.AverageOrderValue);
        Assert.Equal(6, totals.SoldQuantity);
        Assert.Equal(30_000, totals.ShippingDiscount);
        Assert.Equal(1, totals.PaidByMethod.Cod);
        Assert.Equal(3, totals.PaidByMethod.Sepay);
        Assert.Equal(1, totals.Cancellations.ByCustomer);
        Assert.Equal(1, totals.Cancellations.ByShop);
        Assert.Equal(1, totals.Cancellations.PaymentExpired);
        Assert.Equal(0.2m, totals.Cancellations.ByCustomerRate);
        Assert.Equal(0.2m, totals.Cancellations.ByShopRate);
        Assert.Equal(0.2m, totals.Cancellations.PaymentExpiredRate);
        Assert.Equal(0.25m, totals.PaidByMethod.CodRate);
        Assert.Equal(0.75m, totals.PaidByMethod.SepayRate);
        Assert.Equal(3, totals.OrderStatuses.Confirmed);
    }

    [Fact]
    // Khi không có đơn nào thanh toán, giá trị trung bình đơn phải là null để tránh lỗi chia cho 0.
    public void AggregateTotals_WithZeroPaidOrders_ReturnsNullAovEvenWithPlacedOrders()
    {
        var day = CreateDay(
            new DateOnly(2026, 10, 1),
            recognizedRevenue: 0,
            paidOrderCount: 0,
            placedOrderValue: 500_000,
            createdOrderCount: 2);

        var totals = SalesReportAggregator.AggregateTotals([day]);

        Assert.Null(totals.AverageOrderValue);
    }

    [Fact]
    // Các ngày rơi vào cùng một tháng sẽ gộp chung vào nhóm của tháng đó.
    public void AggregateBuckets_WithMonthBuckets_AggregatesDaysIntoRespectiveBuckets()
    {
        var bucketMay = new ReportBucket(
            "2026-05",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            new DateTime(2026, 4, 30, 17, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 31, 17, 0, 0, DateTimeKind.Utc));

        var bucketJune = new ReportBucket(
            "2026-06",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            new DateTime(2026, 5, 31, 17, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 17, 0, 0, DateTimeKind.Utc));

        var dayMay1 = CreateDay(new DateOnly(2026, 5, 1), recognizedRevenue: 100_000, paidOrderCount: 1, soldQuantity: 2);
        var dayMay31 = CreateDay(new DateOnly(2026, 5, 31), recognizedRevenue: 200_000, paidOrderCount: 1, soldQuantity: 3);
        var dayJune1 = CreateDay(new DateOnly(2026, 6, 1), recognizedRevenue: 400_000, paidOrderCount: 2, soldQuantity: 5);

        var buckets = SalesReportAggregator.AggregateBuckets(
            [bucketMay, bucketJune],
            [dayMay1, dayMay31, dayJune1]);

        Assert.Equal(2, buckets.Count);

        var first = buckets[0];
        Assert.Equal("2026-05", first.Key);
        Assert.Equal(new DateOnly(2026, 5, 1), first.From);
        Assert.Equal(new DateOnly(2026, 5, 31), first.To);
        Assert.Equal(300_000, first.RecognizedRevenue);
        Assert.Equal(2, first.PaidOrders);
        Assert.Equal(5, first.SoldQuantity);

        var second = buckets[1];
        Assert.Equal("2026-06", second.Key);
        Assert.Equal(new DateOnly(2026, 6, 1), second.From);
        Assert.Equal(new DateOnly(2026, 6, 30), second.To);
        Assert.Equal(400_000, second.RecognizedRevenue);
        Assert.Equal(2, second.PaidOrders);
        Assert.Equal(5, second.SoldQuantity);
    }

    [Fact]
    // Nhóm thời gian không có ngày bán hàng nào vẫn trả về dòng có các số bằng 0.
    public void AggregateBuckets_WithEmptyBucket_ReturnsZeroBucket()
    {
        var bucket = new ReportBucket(
            "2026-05-01",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 1),
            new DateTime(2026, 4, 30, 17, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 17, 0, 0, DateTimeKind.Utc));

        var buckets = SalesReportAggregator.AggregateBuckets([bucket], []);

        var single = Assert.Single(buckets);
        Assert.Equal("2026-05-01", single.Key);
        Assert.Equal(0, single.RecognizedRevenue);
        Assert.Equal(0, single.PaidOrders);
        Assert.Equal(0, single.SoldQuantity);
        Assert.Equal(0, single.PaidByMethod.Cod);
        Assert.Equal(0, single.PaidByMethod.Sepay);
    }

    [Fact]
    // Các thương hiệu sắp xếp giảm dần theo doanh thu, và nhóm thương hiệu null luôn nằm ở cuối.
    public void AggregateBrands_SortsByRevenueDescending_AndPlacesNullBrandAtTheEnd()
    {
        var productSales = new[]
        {
            new ProductSales("p1", 500_000, 2),
            new ProductSales("p2", 800_000, 4),
            new ProductSales("p3", 300_000, 1),
            new ProductSales("p4", 900_000, 3),
        };

        var catalogMap = new Dictionary<string, ProductSummary>
        {
            ["p1"] = CreateProductSummary("p1", "Bàn phím cơ", "b1", "Logitech"),
            ["p2"] = CreateProductSummary("p2", "Chuột không dây", "b2", "Razer"),
            // p3 có thương hiệu rỗng nên sẽ gộp vào nhóm null
            ["p3"] = CreateProductSummary("p3", "Cáp sạc", string.Empty, string.Empty),
            // p4 không có trong catalog nên cũng sẽ gộp vào nhóm null
        };

        var brands = SalesReportAggregator.AggregateBrands(productSales, catalogMap);

        Assert.Equal(3, brands.Count);

        // b2 (Razer) có doanh thu 800.000 cao nhất trong các thương hiệu có mã.
        Assert.Equal("b2", brands[0].BrandId);
        Assert.Equal("Razer", brands[0].BrandName);
        Assert.Equal(800_000, brands[0].Revenue);
        Assert.Equal(4, brands[0].Quantity);

        // b1 (Logitech) có doanh thu 500.000 đứng thứ hai.
        Assert.Equal("b1", brands[1].BrandId);
        Assert.Equal("Logitech", brands[1].BrandName);
        Assert.Equal(500_000, brands[1].Revenue);

        // Nhóm null gồm p3 (300.000) và p4 (900.000), tổng 1.200.000, dù doanh thu cao hơn vẫn nằm cuối.
        Assert.Null(brands[2].BrandId);
        Assert.Null(brands[2].BrandName);
        Assert.Equal(1_200_000, brands[2].Revenue);
        Assert.Equal(4, brands[2].Quantity);
        Assert.Equal(2, brands[2].Products.Count);
        Assert.Equal("p3", brands[2].Products[0].ProductId);
        Assert.Equal("p4", brands[2].Products[1].ProductId);
        Assert.Null(brands[2].Products[1].Name);
    }

    [Fact]
    // Các danh mục sắp xếp giảm dần theo doanh thu, và nhóm chưa có danh mục luôn nằm ở cuối.
    public void AggregateCategories_SortsByRevenueDescending_AndPlacesUnknownCategoryAtTheEnd()
    {
        var productSales = new[]
        {
            new ProductSales("p1", 500_000, 2),
            new ProductSales("p2", 800_000, 4),
            new ProductSales("p3", 300_000, 1),
            new ProductSales("p4", 900_000, 3),
            new ProductSales("p5", 200_000, 1),
        };

        var categoryMap = new Dictionary<string, CategoryRef>
        {
            ["p1"] = new CategoryRef("c1", "Thiết bị", "thiet-bi"),
            ["p2"] = new CategoryRef("c2", "Phụ kiện", "phu-kien"),
            ["p4"] = new CategoryRef("c1", "Thiết bị", "thiet-bi"),
            ["p5"] = new CategoryRef("c2", "Phụ kiện", "phu-kien"),
            // p3 không có trong catalog nên sẽ gộp vào nhóm null
        };

        var categories = SalesReportAggregator.AggregateCategories(productSales, categoryMap);

        Assert.Equal(3, categories.Count);

        // c1 (Thiết bị) có doanh thu 1.400.000, cao hơn c2 (Phụ kiện) 1.000.000.
        Assert.Equal("c1", categories[0].CategoryId);
        Assert.Equal("Thiết bị", categories[0].CategoryName);
        Assert.Equal(1_400_000, categories[0].Revenue);
        Assert.Equal(5, categories[0].Quantity);

        Assert.Equal("c2", categories[1].CategoryId);
        Assert.Equal("Phụ kiện", categories[1].CategoryName);
        Assert.Equal(1_000_000, categories[1].Revenue);
        Assert.Equal(5, categories[1].Quantity);

        // Nhóm null gồm p3 (300.000) và nằm cuối danh sách.
        Assert.Null(categories[2].CategoryId);
        Assert.Null(categories[2].CategoryName);
        Assert.Equal(300_000, categories[2].Revenue);
        Assert.Equal(1, categories[2].Quantity);
    }

    [Fact]
    // Danh sách sản phẩm bán chạy nhất giữ nguyên thứ tự sắp xếp ban đầu và bổ sung tên cùng thương hiệu.
    public void BuildTopProducts_PreservesInitialOrder_AndFillsMetadataFromCatalog()
    {
        var productSales = new[]
        {
            new ProductSales("p1", 500_000, 2),
            new ProductSales("p2", 300_000, 1),
        };

        var catalogMap = new Dictionary<string, ProductSummary>
        {
            ["p1"] = CreateProductSummary("p1", "Bàn phím cơ", "b1", "Logitech"),
        };

        var topProducts = SalesReportAggregator.BuildTopProducts(productSales, catalogMap);

        Assert.Equal(2, topProducts.Count);

        Assert.Equal("p1", topProducts[0].ProductId);
        Assert.Equal("Bàn phím cơ", topProducts[0].Name);
        Assert.Equal("Logitech", topProducts[0].BrandName);
        Assert.Equal(500_000, topProducts[0].Revenue);
        Assert.Equal(2, topProducts[0].Quantity);

        Assert.Equal("p2", topProducts[1].ProductId);
        Assert.Null(topProducts[1].Name);
        Assert.Null(topProducts[1].BrandName);
        Assert.Equal(300_000, topProducts[1].Revenue);
        Assert.Equal(1, topProducts[1].Quantity);
    }

    [Fact]
    // Hàm Build gộp toàn bộ các thành phần thành cấu trúc hoàn chỉnh.
    public void Build_CombinesAllSections_IntoCompleteReportData()
    {
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 2);
        var bucket = new ReportBucket(
            "2026-10",
            from,
            to,
            new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 2, 17, 0, 0, DateTimeKind.Utc));

        var period = new ReportPeriod(
            from,
            to,
            new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 2, 17, 0, 0, DateTimeKind.Utc),
            ReportGranularity.Month,
            [bucket]);

        var day = CreateDay(from, recognizedRevenue: 100_000, paidOrderCount: 1, soldQuantity: 1);
        var productSales = new[] { new ProductSales("p1", 100_000, 1) };
        var catalogMap = new Dictionary<string, ProductSummary>
        {
            ["p1"] = CreateProductSummary("p1", "Tai nghe", "b1", "Sony"),
        };
        var categoryMap = new Dictionary<string, CategoryRef>
        {
            ["p1"] = new CategoryRef("c1", "Âm thanh", "am-thanh"),
        };

        var report = SalesReportAggregator.Build(period, [day], productSales, catalogMap, categoryMap);

        Assert.Equal(from, report.From);
        Assert.Equal(to, report.To);
        Assert.Equal("month", report.Granularity);
        Assert.Equal(100_000, report.Totals.RecognizedRevenue);
        Assert.Single(report.Buckets);
        Assert.Single(report.Brands);
        Assert.Equal("Âm thanh", Assert.Single(report.TopCategories).CategoryName);
        Assert.Single(report.TopProducts);
    }
}
