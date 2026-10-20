namespace Pickory.Modules.ActivityTracking.Contracts.Metrics;

// `ConversionRate` là số lượt bước này chia số lượt bước trước. Giá trị `null` nghĩa là bước đầu, hoặc bước trước có 0 lượt.
public sealed record FunnelStep(string Metric, long Count, decimal? ConversionRate);
