using Microsoft.EntityFrameworkCore;
using Pickory.Modules.ActivityTracking.Contracts.Metrics;
using Pickory.Modules.ActivityTracking.Persistence;

namespace Pickory.Modules.ActivityTracking.Metrics;

// Cài cổng số liệu cho module khác. Lớp chỉ đọc số đếm gộp theo ngày, nên không trả dữ liệu theo từng khách.
internal sealed class ActivityMetrics(ActivityTrackingDbContext database) : IActivityMetrics
{
    public async Task<Funnel> GetFunnelAsync(DateRange range, CancellationToken cancellationToken)
    {
        // Truy vấn cộng theo chỉ số ngay trong DB, nên ứng dụng chỉ nhận một dòng cho mỗi chỉ số dù khoảng dài 366 ngày. Cả hai đầu ngày đều tính.
        var totalsByMetric = await database.DailyCounters
            .AsNoTracking()
            .Where(counter => counter.Date >= range.From && counter.Date <= range.To)
            .GroupBy(counter => counter.Metric)
            .Select(group => new { Metric = group.Key, Total = group.Sum(counter => counter.Value) })
            .ToDictionaryAsync(row => row.Metric, row => row.Total, cancellationToken);

        // Chỉ số không có dòng nào trong khoảng thì tổng là 0, để phễu luôn đủ ba bước.
        return FunnelCalculator.Calculate(
            productViews: totalsByMetric.GetValueOrDefault(ActivityMetricNames.ProductView),
            addToCarts: totalsByMetric.GetValueOrDefault(ActivityMetricNames.AddToCart),
            ordersPlaced: totalsByMetric.GetValueOrDefault(ActivityMetricNames.OrderPlaced));
    }
}
