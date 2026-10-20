namespace Pickory.Modules.ActivityTracking.Contracts.Metrics;

// Cổng số liệu hành vi cho `admin-console`. Cổng chỉ trả số đếm gộp theo ngày, không trả dữ liệu theo từng khách.
public interface IActivityMetrics
{
    // Trả phễu xem sản phẩm, thêm giỏ, đặt đơn cộng dồn trong khoảng ngày.
    Task<Funnel> GetFunnelAsync(DateRange range, CancellationToken cancellationToken);
}
