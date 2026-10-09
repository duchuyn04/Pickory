namespace Pickory.Modules.AiGateway.Usage;

// Báo cáo usage cho trang quản trị. `Calls` và p95 đếm theo bước gateway, gồm cả bước bị chặn và trúng cache.
public sealed record UsageSummary(
    double CostUsd,
    int Calls,
    double ErrorRate,
    int P95LatencyMs,
    IReadOnlyList<DayUsage> ByDay,
    IReadOnlyList<FeatureUsage> ByFeature,
    bool BudgetWarning);
