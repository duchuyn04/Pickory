namespace Pickory.Modules.AiGateway.Entities;

public sealed class AiSetting
{
    public string Id { get; set; } = "singleton";
    public double MonthlyBudgetUsd { get; set; }
    public int ChatDailyLimitUser { get; set; }
    public int ChatDailyLimitAnon { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
