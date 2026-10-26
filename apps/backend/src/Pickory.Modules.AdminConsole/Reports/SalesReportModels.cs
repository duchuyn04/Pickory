namespace Pickory.Modules.AdminConsole.Reports;

// Số đơn theo từng phương thức thanh toán trong báo cáo doanh thu.
internal sealed record SalesReportPaymentMethods(int Cod, int Sepay);

// Số đơn hủy theo từng lý do trong báo cáo doanh thu.
internal sealed record SalesReportCancellations(int ByCustomer, int ByShop, int PaymentExpired);

// Tổng hợp số liệu toàn bộ khoảng thời gian của báo cáo doanh thu.
internal sealed record SalesReportTotals(
    long RecognizedRevenue,
    long PlacedOrderValue,
    int PaidOrders,
    int CreatedOrders,
    long? AverageOrderValue,
    int SoldQuantity,
    long ShippingDiscount,
    SalesReportPaymentMethods PaidByMethod,
    SalesReportCancellations Cancellations);

// Số liệu của một nhóm thời gian trong báo cáo doanh thu.
internal sealed record SalesReportBucket(
    string Key,
    DateOnly From,
    DateOnly To,
    long RecognizedRevenue,
    int PaidOrders,
    SalesReportPaymentMethods PaidByMethod,
    SalesReportCancellations Cancellations,
    int SoldQuantity);

// Doanh thu và số lượng bán của một sản phẩm trong nhóm thương hiệu.
internal sealed record SalesReportProduct(
    string ProductId,
    string? Name,
    long Revenue,
    int Quantity);

// Doanh thu và số lượng bán gộp theo thương hiệu.
internal sealed record SalesReportBrand(
    string? BrandId,
    string? BrandName,
    long Revenue,
    int Quantity,
    IReadOnlyList<SalesReportProduct> Products);

// Doanh thu và số lượng của sản phẩm bán chạy nhất.
internal sealed record SalesReportTopProduct(
    string ProductId,
    string? Name,
    string? BrandName,
    long Revenue,
    int Quantity);

// Dữ liệu hoàn chỉnh trả về từ API báo cáo doanh thu.
internal sealed record SalesReportData(
    DateOnly From,
    DateOnly To,
    string Granularity,
    SalesReportTotals Totals,
    IReadOnlyList<SalesReportBucket> Buckets,
    IReadOnlyList<SalesReportBrand> Brands,
    IReadOnlyList<SalesReportTopProduct> TopProducts);
