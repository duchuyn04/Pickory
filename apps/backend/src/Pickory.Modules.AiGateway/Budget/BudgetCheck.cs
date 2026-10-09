namespace Pickory.Modules.AiGateway.Budget;

// Kết quả kiểm tra ngân sách. `BudgetWarning` bật khi chi phí tháng đạt 80% ngân sách.
public sealed record BudgetCheck(double TotalCostUsd, bool BudgetWarning);
