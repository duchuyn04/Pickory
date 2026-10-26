using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Reports;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Calendar;
using Pickory.Shared.Http;

namespace Pickory.Modules.AdminConsole.Reports;

// Điểm cuối cung cấp dữ liệu báo cáo doanh thu cho trang quản trị.
internal static class SalesReportEndpoints
{
    // Tập trung đăng ký route tại module để `Program.cs` không phụ thuộc chi tiết báo cáo.
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Quyền `reports:read` được kiểm tra trước khi handler thực thi.
        endpoints.MapGet("/api/admin/reports/sales", GetSalesReportAsync)
            .RequirePermission(Permissions.ReportsRead);
    }

    // Đọc và tổng hợp số liệu báo cáo doanh thu theo bộ lọc thời gian.
    internal static async Task<IResult> GetSalesReportAsync(
        HttpContext context,
        ICommerceReports commerceReports,
        ICatalogReader catalogReader,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Lấy ngày qua `TimeProvider` để test đặt được đồng hồ. Ngày báo cáo đổi lúc nửa đêm giờ Việt Nam.
        var today = VietnamCalendar.ToVietnamDate(timeProvider.GetUtcNow().UtcDateTime);

        // Kiểm tra toàn bộ query string trước khi gọi sang các module khác.
        var failures = new List<ValidationFailure>();
        var period = ReportRange.Read(context.Request.Query, today, failures);
        if (period is null)
        {
            throw new ValidationException(failures);
        }

        // Hợp đồng commerce nhận ngày lịch Việt Nam. Khoảng đã kiểm không được đổi sang ngày UTC.
        var commerceRange = new DateRange(period.From, period.To);

        // Đọc số liệu bán hàng theo ngày từ module commerce một lần cho cả khoảng.
        var dailySales = await commerceReports.GetDailySalesAsync(commerceRange, cancellationToken);

        // Đọc số liệu bán hàng theo sản phẩm từ module commerce một lần cho cả khoảng.
        var productSales = await commerceReports.GetProductSalesAsync(commerceRange, cancellationToken);

        // Loại mã trùng để catalog nhận đúng một lần tra cứu cho mỗi sản phẩm.
        var productIds = productSales.Select(product => product.ProductId).Distinct().ToList();

        // Đọc thông tin tên và thương hiệu từ module catalog một lần cho mọi sản phẩm.
        var catalogProducts = await catalogReader.GetProductsAsync(productIds, cancellationToken);
        // Giữ bản ghi đầu nếu catalog trả trùng mã. Báo cáo không được lỗi chỉ vì dữ liệu đọc lặp.
        var catalogMap = catalogProducts.DistinctBy(product => product.Id).ToDictionary(product => product.Id);

        // Tách phép gộp khỏi handler để kiểm tra bằng dữ liệu thuần. Handler chỉ điều phối các cổng đọc.
        var reportData = SalesReportAggregator.Build(period, dailySales, productSales, catalogMap);

        return Results.Ok(new ApiResponse<SalesReportData>(reportData));
    }
}
