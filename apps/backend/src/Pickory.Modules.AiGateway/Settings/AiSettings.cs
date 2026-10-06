namespace Pickory.Modules.AiGateway.Settings;

// Ngân sách tháng và hạn mức chat theo ngày. Admin sửa trên giao diện, giá trị có hiệu lực ở lời gọi kế tiếp.
public sealed record AiSettings(double MonthlyBudgetUsd, int ChatDailyLimitUser, int ChatDailyLimitAnon);
