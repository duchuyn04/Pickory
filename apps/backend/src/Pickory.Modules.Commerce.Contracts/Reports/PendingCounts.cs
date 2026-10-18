namespace Pickory.Modules.Commerce.Contracts.Reports;

// Số việc đang chờ nhân viên làm, đếm tại thời điểm gọi.
public sealed record PendingCounts(int PendingConfirmation, int UnmatchedPayments, int CodUnreceived);
