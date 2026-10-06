using Microsoft.EntityFrameworkCore;
using Pickory.Modules.AiGateway.Calendar;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.AiGateway.Settings;

namespace Pickory.Modules.AiGateway.Usage;

// Tổng hợp `ai_usage_logs` thành báo cáo chi phí, lỗi và độ trễ cho trang quản trị.
public sealed class UsageSummaryReader(
    IDbContextFactory<AiGatewayDbContext> databaseFactory,
    AiSettingsStore settingsStore,
    TimeProvider timeProvider)
{
    private const double WarningRatio = 0.8;

    // Khoảng thời gian gồm cả hai mốc. Route giới hạn khoảng tối đa 92 ngày, nên dữ liệu đọc vào bộ nhớ có giới hạn.
    public async Task<UsageSummary> ReadAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var logs = await database.UsageLogs.AsNoTracking()
            .Where(log => log.CreatedAt >= fromUtc && log.CreatedAt <= toUtc)
            .OrderBy(log => log.CreatedAt)
            .Select(log => new { log.CostUsd, log.LatencyMs, log.Status, log.Feature, log.CreatedAt })
            .ToListAsync(cancellationToken);

        // Cộng chi phí gốc, không làm tròn. Chi phí embedding rất nhỏ vẫn hiện trong báo cáo.
        var byDay = logs
            .GroupBy(log => VietnamCalendar.DayKey(new DateTimeOffset(DateTime.SpecifyKind(log.CreatedAt, DateTimeKind.Utc))))
            .Select(group => new DayUsage(group.Key, group.Sum(log => log.CostUsd), group.Count()))
            .OrderBy(group => group.Day, StringComparer.Ordinal)
            .ToList();
        // Tính năng tốn nhiều nhất lên đầu. Chi phí bằng nhau thì xếp theo tên để thứ tự cố định.
        var byFeature = logs
            .GroupBy(log => log.Feature)
            .Select(group => new FeatureUsage(group.Key, group.Sum(log => log.CostUsd), group.Count()))
            .OrderByDescending(group => group.CostUsd)
            .ThenBy(group => group.Feature, StringComparer.Ordinal)
            .ToList();

        var calls = logs.Count;
        // Tỉ lệ lỗi chỉ đếm trạng thái `error`. Bước bị chặn không phải lỗi của provider.
        var errorCalls = logs.Count(log => log.Status == "error");
        var errorRate = calls > 0 ? (double)errorCalls / calls : 0;
        var p95LatencyMs = NearestRankPercentile.Calculate(logs.Select(log => log.LatencyMs).ToList(), 95);

        return new UsageSummary(
            logs.Sum(log => log.CostUsd),
            calls,
            errorRate,
            p95LatencyMs,
            byDay,
            byFeature,
            await IsBudgetWarningAsync(database, cancellationToken));
    }

    // Cảnh báo so chi phí cả tháng hiện tại với ngân sách, không chỉ chi phí trong khoảng báo cáo.
    private async Task<bool> IsBudgetWarningAsync(AiGatewayDbContext database, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        var window = VietnamCalendar.MonthWindow(timeProvider.GetUtcNow());
        var monthCostUsd = await database.UsageLogs
            .Where(log => log.CreatedAt >= window.StartUtc && log.CreatedAt < window.NextStartUtc)
            .SumAsync(log => log.CostUsd, cancellationToken);

        // Ngân sách bằng 0 nghĩa là admin tắt AI. Mọi chi phí phát sinh lúc đó đều cần cảnh báo.
        return settings.MonthlyBudgetUsd > 0
            ? monthCostUsd >= settings.MonthlyBudgetUsd * WarningRatio
            : monthCostUsd > 0;
    }
}
