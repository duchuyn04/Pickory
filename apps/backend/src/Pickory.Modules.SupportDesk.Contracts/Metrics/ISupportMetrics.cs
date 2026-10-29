namespace Pickory.Modules.SupportDesk.Contracts.Metrics;

// Cổng số liệu CSKH cho `admin-console`. Cổng chỉ trả số gộp, không trả nội dung chat hay số điện thoại của khách.
public interface ISupportMetrics
{
    // Trả số liệu CSKH trong khoảng ngày. Số liệu gồm phiên chuyển, phiên trong giờ, yêu cầu gọi lại, thời gian phản hồi và CSAT.
    Task<SupportMetricsSnapshot> GetAsync(DateRange range, CancellationToken cancellationToken);

    // Trả số phiên đang chờ và số yêu cầu gọi lại còn mở. Trang tổng quan dùng hai số này để hiện việc cần làm.
    Task<SupportPendingCounts> GetPendingCountsAsync(CancellationToken cancellationToken);
}
