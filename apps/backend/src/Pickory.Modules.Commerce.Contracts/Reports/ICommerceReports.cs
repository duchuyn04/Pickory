namespace Pickory.Modules.Commerce.Contracts.Reports;

// Cổng số liệu commerce cho `admin-console`, ngày tính theo giờ Việt Nam (UTC+7).
// Hai hàm đầu ném `ArgumentException` khi `From` sau `To` hoặc khoảng dài hơn 366 ngày.
public interface ICommerceReports
{
    // Trả đúng một dòng cho mỗi ngày trong khoảng, sắp tăng dần. Ngày không có số liệu vẫn có dòng với các số bằng 0.
    Task<IReadOnlyList<DailySales>> GetDailySalesAsync(DateRange range, CancellationToken cancellationToken);

    // Gộp theo sản phẩm các đơn có doanh thu ghi nhận trong khoảng, không gồm phí ship.
    // Sắp `Revenue` giảm dần, rồi `Quantity` giảm dần, rồi `ProductId` theo ordinal, và không giới hạn số dòng.
    Task<IReadOnlyList<ProductSales>> GetProductSalesAsync(DateRange range, CancellationToken cancellationToken);

    // Đếm việc đang chờ: đơn chờ xác nhận, giao dịch chưa khớp và đơn COD đã giao nhưng chưa nhận tiền.
    Task<PendingCounts> GetPendingCountsAsync(CancellationToken cancellationToken);
}
