namespace Pickory.Modules.SupportDesk.Contracts.Metrics;

// Điểm hài lòng trung bình và số lượt chấm. `Average` là `null` khi `Count` bằng 0, để báo cáo không hiện điểm 0 sai.
public sealed record CsatSummary(double? Average, int Count);
