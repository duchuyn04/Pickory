namespace Pickory.Modules.SupportDesk.Contracts.Metrics;

// Phân vị p50 và p90 của một số đo. Giá trị là `null` khi khoảng ngày chưa có phiên nào để đo.
public sealed record PercentileSummary(double? P50, double? P90);
