namespace Pickory.Modules.AiGateway.Persistence;

public sealed class AiUsageLog
{
    public string Id { get; set; } = null!;
    public required string Feature { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public string? PromptVersion { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CachedTokens { get; set; }
    public double CostUsd { get; set; }
    public int LatencyMs { get; set; }
    public required string Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ActorKeyHash { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AiSetting
{
    public string Id { get; set; } = "singleton";
    public double MonthlyBudgetUsd { get; set; }
    public int ChatDailyLimitUser { get; set; }
    public int ChatDailyLimitAnon { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AiEmbeddingCache
{
    public string Id { get; set; } = null!;
    public required string Key { get; set; }
    public required string Model { get; set; }
    // Migration Node cho phép NULL. Không đổi cột thành `vector` hoặc bắt buộc giá trị.
    public double[]? Embedding { get; set; }
    public DateTime CreatedAt { get; set; }
}
