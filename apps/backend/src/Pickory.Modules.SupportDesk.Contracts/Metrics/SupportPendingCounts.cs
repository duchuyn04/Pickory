namespace Pickory.Modules.SupportDesk.Contracts.Metrics;

// Số việc đang chờ nhân viên. Gồm phiên chưa có người nhận và yêu cầu gọi lại còn mở.
public sealed record SupportPendingCounts(int Waiting, int Callbacks);
