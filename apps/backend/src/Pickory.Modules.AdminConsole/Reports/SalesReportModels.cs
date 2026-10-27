namespace Pickory.Modules.AdminConsole.Reports;

// Số đơn đã thanh toán theo từng phương thức trong báo cáo doanh thu.
internal sealed record SalesReportPaymentMethods(int Cod, int Sepay);

// Số đơn và tỉ lệ đã thanh toán theo phương thức trong toàn bộ khoảng báo cáo.
internal sealed record SalesReportPaymentMethodTotals(int Cod, int Sepay, decimal? CodRate, decimal? SepayRate);

// Số đơn hủy theo từng lý do trong báo cáo doanh thu.
internal sealed record SalesReportCancellations(int ByCustomer, int ByShop, int PaymentExpired);

// Số đơn và tỉ lệ hủy theo lý do trong toàn bộ khoảng báo cáo.
internal sealed record SalesReportCancellationTotals(
    int ByCustomer,
    int ByShop,
    int PaymentExpired,
    decimal? ByCustomerRate,
    decimal? ByShopRate,
    decimal? PaymentExpiredRate);

// Số đơn theo trạng thái hiện tại của các đơn tạo trong kỳ.
internal sealed record SalesReportOrderStatuses(
    int PendingPayment,
    int PendingConfirmation,
    int Confirmed,
    int Shipping,
    int Delivered,
    int Returned,
    int Cancelled);

// Tổng hợp số liệu toàn bộ khoảng thời gian của báo cáo doanh thu.
internal sealed record SalesReportTotals(
    long RecognizedRevenue,
    long PlacedOrderValue,
    int PaidOrders,
    int CreatedOrders,
    long? AverageOrderValue,
    int SoldQuantity,
    long ShippingDiscount,
    SalesReportPaymentMethodTotals PaidByMethod,
    SalesReportCancellationTotals Cancellations,
    SalesReportOrderStatuses OrderStatuses);

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

// Doanh thu và số lượng bán gộp theo danh mục lá. `CategoryId` là `null` khi sản phẩm không có trong catalog.
internal sealed record SalesReportCategory(
    string? CategoryId,
    string? CategoryName,
    long Revenue,
    int Quantity);

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
    IReadOnlyList<SalesReportCategory> TopCategories,
    IReadOnlyList<SalesReportTopProduct> TopProducts);
