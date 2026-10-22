namespace Pickory.Modules.SupportDesk.Contracts.Metrics;

// Số liệu CSKH trong một khoảng ngày. Thời gian phản hồi đầu tiên tính bằng giây, thời gian giải quyết tính bằng phút.
public sealed record SupportMetricsSnapshot(
    int Handoffs,
    int HandledInHours,
    int CallbacksRequested,
    int CallbacksCompleted,
    PercentileSummary FirstResponseSeconds,
    PercentileSummary HandleMinutes,
    CsatSummary Csat);
