namespace Pickory.Modules.AiGateway.Entities;

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
