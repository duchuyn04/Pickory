using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;

namespace Pickory.Modules.AiGateway.Usage;

// Một dòng `ai_usage_logs`. Dòng log không chứa nội dung prompt hay câu trả lời.
public sealed record UsageLogEntry(AiFeature Feature, string Provider, string Model, UsageStatus Status)
{
    public string? PromptVersion { get; init; }
    public TokenUsage? Usage { get; init; }
    // Null thì writer tự tính từ `Usage`. Lời gọi bị chặn hoặc lỗi trước provider truyền 0.
    public double? CostUsd { get; init; }
    public double LatencyMs { get; init; }
    public string? ErrorCode { get; init; }
    // Writer chỉ lưu hash SHA-256 của khóa này, không lưu ID gốc.
    public string? ActorKey { get; init; }
    public DateTime? CreatedAt { get; init; }
}
