using Pickory.Modules.ActivityTracking.Contracts.Metrics;

namespace Pickory.Modules.ActivityTracking.Features.AdminAnalytics;

// Dạng JSON của phễu. `ConversionRate` là số thập phân từ 0, ví dụ 0.25 là 25%, và giao diện tự đổi sang phần trăm.
internal sealed record FunnelResponse(DateOnly From, DateOnly To, IReadOnlyList<FunnelStepResponse> Steps)
{
    public static FunnelResponse Create(DateRange range, Funnel funnel)
    {
        var steps = funnel.Steps
            .Select(step => new FunnelStepResponse(step.Metric, step.Count, step.ConversionRate))
            .ToList();
        return new FunnelResponse(range.From, range.To, steps);
    }
}

internal sealed record FunnelStepResponse(string Metric, long Count, decimal? ConversionRate);
