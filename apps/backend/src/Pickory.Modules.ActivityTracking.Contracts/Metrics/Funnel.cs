namespace Pickory.Modules.ActivityTracking.Contracts.Metrics;

// Phễu chuyển đổi: danh sách bước theo thứ tự xem sản phẩm, thêm giỏ, đặt đơn.
public sealed record Funnel(IReadOnlyList<FunnelStep> Steps);
