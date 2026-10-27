using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Reports;

namespace Pickory.Modules.AdminConsole.Reports;

// Gom số liệu từ cổng đọc thành cấu trúc hoàn chỉnh cho báo cáo doanh thu.
internal static class SalesReportAggregator
{
    // Tách phép gộp khỏi route để kiểm tra bằng dữ liệu thuần. Các nguồn đã nạp dùng chung một khoảng báo cáo.
    public static SalesReportData Build(
        ReportPeriod period,
        IReadOnlyList<DailySales> dailySales,
        IReadOnlyList<ProductSales> productSales,
        IReadOnlyDictionary<string, ProductSummary> catalogMap,
        IReadOnlyDictionary<string, CategoryRef> categoryMap)
    {
        // Tính tổng hợp toàn bộ khoảng thời gian báo cáo.
        var totals = AggregateTotals(dailySales);

        // Gộp số liệu từng ngày vào từng nhóm thời gian theo độ chia.
        var buckets = AggregateBuckets(period.Buckets, dailySales);

        // Gộp doanh thu và số lượng của từng sản phẩm theo thương hiệu.
        var brands = AggregateBrands(productSales, catalogMap);

        // Gộp doanh thu và số lượng của từng sản phẩm theo danh mục lá.
        var topCategories = AggregateCategories(productSales, categoryMap);

        // Lấy danh sách sản phẩm bán chạy nhất giữ nguyên thứ tự sắp xếp từ cổng đọc.
        var topProducts = BuildTopProducts(productSales, catalogMap);

        return new SalesReportData(
            period.From,
            period.To,
            FormatGranularity(period.Granularity),
            totals,
            buckets,
            brands,
            topCategories,
            topProducts);
    }

    // Tính tổng các chỉ số từ danh sách số liệu theo ngày.
    public static SalesReportTotals AggregateTotals(IReadOnlyList<DailySales> dailySales)
    {
        long recognizedRevenue = 0;
        long placedOrderValue = 0;
        var paidOrders = 0;
        var createdOrders = 0;
        var soldQuantity = 0;
        long shippingDiscount = 0;
        var codOrders = 0;
        var sepayOrders = 0;
        var customerCancellations = 0;
        var shopCancellations = 0;
        var paymentExpiredCancellations = 0;
        var pendingPaymentOrders = 0;
        var pendingConfirmationOrders = 0;
        var confirmedOrders = 0;
        var shippingOrders = 0;
        var deliveredOrders = 0;
        var returnedOrders = 0;
        var cancelledOrders = 0;

        foreach (var day in dailySales)
        {
            // Cộng mọi chỉ số ngày vào cùng tổng để dashboard nhận đủ doanh thu, đơn, hủy và giảm giá vận chuyển.
            recognizedRevenue += day.RecognizedRevenue;
            placedOrderValue += day.PlacedOrderValue;
            paidOrders += day.PaidOrderCount;
            createdOrders += day.CreatedOrderCount;
            soldQuantity += day.SoldQuantity;
            shippingDiscount += day.ShippingDiscount;
            codOrders += day.PaymentMethods.Cod;
            sepayOrders += day.PaymentMethods.Sepay;
            customerCancellations += day.Cancellations.ByCustomer;
            shopCancellations += day.Cancellations.ByShop;
            paymentExpiredCancellations += day.Cancellations.PaymentExpired;
            // Cộng từng trạng thái riêng để hai đơn cùng ngày không bị gộp mất trạng thái hiện tại.
            pendingPaymentOrders += day.OrderStatuses.PendingPayment;
            pendingConfirmationOrders += day.OrderStatuses.PendingConfirmation;
            confirmedOrders += day.OrderStatuses.Confirmed;
            shippingOrders += day.OrderStatuses.Shipping;
            deliveredOrders += day.OrderStatuses.Delivered;
            returnedOrders += day.OrderStatuses.Returned;
            cancelledOrders += day.OrderStatuses.Cancelled;
        }

        // Giá trị trung bình đơn trả null khi không có đơn nào thanh toán để tránh chia cho 0.
        long? averageOrderValue = paidOrders == 0 ? null : recognizedRevenue / paidOrders;
        // Tỉ lệ phương thức dùng tổng đơn đã thanh toán làm mẫu số. Commerce chỉ đếm dòng đủ điều kiện ghi nhận doanh thu.
        var paymentOrders = codOrders + sepayOrders;

        return new SalesReportTotals(
            recognizedRevenue,
            placedOrderValue,
            paidOrders,
            createdOrders,
            averageOrderValue,
            soldQuantity,
            shippingDiscount,
            new SalesReportPaymentMethodTotals(
                codOrders,
                sepayOrders,
                CalculateRate(codOrders, paymentOrders),
                CalculateRate(sepayOrders, paymentOrders)),
            new SalesReportCancellationTotals(
                customerCancellations,
                shopCancellations,
                paymentExpiredCancellations,
                CalculateRate(customerCancellations, createdOrders),
                CalculateRate(shopCancellations, createdOrders),
                CalculateRate(paymentExpiredCancellations, createdOrders)),
            new SalesReportOrderStatuses(
                pendingPaymentOrders,
                pendingConfirmationOrders,
                confirmedOrders,
                shippingOrders,
                deliveredOrders,
                returnedOrders,
                cancelledOrders));
    }

    private static decimal? CalculateRate(int numerator, int denominator)
    {
        // Trả `null` khi mẫu số bằng 0 vì tỉ lệ không xác định, không phải tỉ lệ 0.
        return denominator == 0 ? null : (decimal)numerator / denominator;
    }

    // Gộp số liệu từng ngày vào từng nhóm thời gian tương ứng.
    public static IReadOnlyList<SalesReportBucket> AggregateBuckets(
        IReadOnlyList<ReportBucket> buckets,
        IReadOnlyList<DailySales> dailySales)
    {
        // Cổng trả đúng một dòng mỗi ngày nên bản đồ không làm mất số liệu. Tra cứu theo ngày tránh đọc lại danh sách.
        var dailyMap = dailySales.ToDictionary(day => day.Day);
        var result = new List<SalesReportBucket>(buckets.Count);

        foreach (var bucket in buckets)
        {
            long recognizedRevenue = 0;
            var paidOrders = 0;
            var soldQuantity = 0;
            var codOrders = 0;
            var sepayOrders = 0;
            var customerCancellations = 0;
            var shopCancellations = 0;
            var paymentExpiredCancellations = 0;

            // Các nhóm phủ kín khoảng báo cáo. Duyệt cả ngày đầu và ngày cuối để không bỏ sót số liệu ở biên nhóm.
            for (var current = bucket.From; current <= bucket.To; current = current.AddDays(1))
            {
                if (dailyMap.TryGetValue(current, out var day))
                {
                    recognizedRevenue += day.RecognizedRevenue;
                    paidOrders += day.PaidOrderCount;
                    soldQuantity += day.SoldQuantity;
                    codOrders += day.PaymentMethods.Cod;
                    sepayOrders += day.PaymentMethods.Sepay;
                    customerCancellations += day.Cancellations.ByCustomer;
                    shopCancellations += day.Cancellations.ByShop;
                    paymentExpiredCancellations += day.Cancellations.PaymentExpired;
                }
            }

            result.Add(new SalesReportBucket(
                bucket.Key,
                bucket.From,
                bucket.To,
                recognizedRevenue,
                paidOrders,
                new SalesReportPaymentMethods(codOrders, sepayOrders),
                new SalesReportCancellations(customerCancellations, shopCancellations, paymentExpiredCancellations),
                soldQuantity));
        }

        return result;
    }

    // Gộp sản phẩm theo thương hiệu và sắp xếp giảm dần theo doanh thu.
    public static IReadOnlyList<SalesReportBrand> AggregateBrands(
        IReadOnlyList<ProductSales> productSales,
        IReadOnlyDictionary<string, ProductSummary> catalogMap)
    {
        // Nhóm gom các sản phẩm của các thương hiệu đã biết mã.
        var knownBrands = new Dictionary<string, BrandAccumulator>(StringComparer.Ordinal);
        BrandAccumulator? otherBrand = null;

        foreach (var item in productSales)
        {
            var (brandId, brandName, productName) = ResolveCatalogInfo(item.ProductId, catalogMap);

            if (brandId is null)
            {
                otherBrand ??= new BrandAccumulator(null, null);
                // Nhóm chung giữ doanh thu của sản phẩm thiếu thương hiệu thay vì loại khỏi báo cáo.
                otherBrand.AddProduct(item.ProductId, productName, item.Revenue, item.Quantity);
            }
            else
            {
                if (!knownBrands.TryGetValue(brandId, out var accumulator))
                {
                    accumulator = new BrandAccumulator(brandId, brandName);
                    knownBrands[brandId] = accumulator;
                }

                // Cộng vào đúng thương hiệu để doanh thu và số lượng không bị tách khỏi tên catalog.
                accumulator.AddProduct(item.ProductId, productName, item.Revenue, item.Quantity);
            }
        }

        // Nhóm thương hiệu null luôn nằm cuối danh sách, các nhóm khác xếp giảm dần theo doanh thu.
        var sortedKnown = knownBrands.Values
            .OrderByDescending(brand => brand.Revenue)
            .ThenByDescending(brand => brand.Quantity)
            .ThenBy(brand => brand.BrandName, StringComparer.Ordinal)
            .Select(brand => brand.ToReportBrand());

        return otherBrand is null
            ? sortedKnown.ToList()
            : sortedKnown.Append(otherBrand.ToReportBrand()).ToList();
    }

    // Gộp doanh thu và số lượng theo danh mục lá, sắp giảm dần và đưa nhóm `null` về cuối.
    public static IReadOnlyList<SalesReportCategory> AggregateCategories(
        IReadOnlyList<ProductSales> productSales,
        IReadOnlyDictionary<string, CategoryRef> categoryMap)
    {
        var knownCategories = new Dictionary<string, CategoryAccumulator>(StringComparer.Ordinal);
        long unknownRevenue = 0;
        var unknownQuantity = 0;
        var hasUnknownProducts = false;

        foreach (var item in productSales)
        {
            if (!categoryMap.TryGetValue(item.ProductId, out var category))
            {
                // Sản phẩm catalog không trả về vẫn giữ doanh thu trong báo cáo thay vì bị bỏ.
                unknownRevenue += item.Revenue;
                unknownQuantity += item.Quantity;
                hasUnknownProducts = true;
                continue;
            }

            if (!knownCategories.TryGetValue(category.Id, out var accumulator))
            {
                accumulator = new CategoryAccumulator(category.Id, category.Name);
                knownCategories[category.Id] = accumulator;
            }

            // Cộng vào đúng danh mục lá để doanh thu và số lượng không bị tách khỏi tên catalog.
            accumulator.Add(item.Revenue, item.Quantity);
        }

        // Danh mục có doanh thu cao đứng trước. Tên danh mục phá thế hòa để thứ tự luôn xác định.
        var sortedKnown = knownCategories.Values
            .OrderByDescending(category => category.Revenue)
            .ThenByDescending(category => category.Quantity)
            .ThenBy(category => category.CategoryName, StringComparer.Ordinal)
            .Select(category => category.ToReportCategory());

        return hasUnknownProducts
            ? sortedKnown.Append(new SalesReportCategory(null, null, unknownRevenue, unknownQuantity)).ToList()
            : sortedKnown.ToList();
    }

    // Dựng danh sách sản phẩm bán chạy nhất kèm tên và thương hiệu.
    public static IReadOnlyList<SalesReportTopProduct> BuildTopProducts(
        IReadOnlyList<ProductSales> productSales,
        IReadOnlyDictionary<string, ProductSummary> catalogMap)
    {
        var result = new List<SalesReportTopProduct>(productSales.Count);

        foreach (var item in productSales)
        {
            // Dùng cùng luật đọc catalog với nhóm thương hiệu để tên `null` nhất quán khi sản phẩm bị xóa.
            var (_, brandName, productName) = ResolveCatalogInfo(item.ProductId, catalogMap);

            result.Add(new SalesReportTopProduct(
                item.ProductId,
                productName,
                brandName,
                item.Revenue,
                item.Quantity));
        }

        return result;
    }

    private static (string? BrandId, string? BrandName, string? ProductName) ResolveCatalogInfo(
        string productId,
        IReadOnlyDictionary<string, ProductSummary> catalogMap)
    {
        catalogMap.TryGetValue(productId, out var summary);
        // Gộp mã thương hiệu rỗng vào `null`. Sản phẩm catalog không trả về cũng vào nhóm này để báo cáo không mất doanh thu.
        var brandId = string.IsNullOrWhiteSpace(summary?.Brand?.Id) ? null : summary.Brand.Id;
        var brandName = brandId is null ? null : summary?.Brand?.Name;
        return (brandId, brandName, summary?.Name);
    }

    private static string FormatGranularity(ReportGranularity granularity) =>
        granularity switch
        {
            ReportGranularity.Day => "day",
            ReportGranularity.Week => "week",
            ReportGranularity.Month => "month",
            _ => granularity.ToString().ToLowerInvariant(),
        };

    // Bộ tích lũy số liệu của một thương hiệu.
    private sealed class BrandAccumulator(string? brandId, string? brandName)
    {
        public string? BrandId => brandId;
        public string? BrandName => brandName;
        public long Revenue { get; private set; }
        public int Quantity { get; private set; }
        private readonly List<SalesReportProduct> _products = [];

        public void AddProduct(string productId, string? name, long revenue, int quantity)
        {
            // Cộng dồn doanh thu và số lượng bán cho thương hiệu.
            Revenue += revenue;
            Quantity += quantity;
            _products.Add(new SalesReportProduct(productId, name, revenue, quantity));
        }

        public SalesReportBrand ToReportBrand() => new(brandId, brandName, Revenue, Quantity, _products);
    }

    // Bộ tích lũy số liệu của một danh mục lá.
    private sealed class CategoryAccumulator(string categoryId, string categoryName)
    {
        public string CategoryId => categoryId;
        public string CategoryName => categoryName;
        public long Revenue { get; private set; }
        public int Quantity { get; private set; }

        public void Add(long revenue, int quantity)
        {
            // Cộng dồn doanh thu và số lượng của mọi sản phẩm trong danh mục.
            Revenue += revenue;
            Quantity += quantity;
        }

        public SalesReportCategory ToReportCategory() => new(categoryId, categoryName, Revenue, Quantity);
    }
}
