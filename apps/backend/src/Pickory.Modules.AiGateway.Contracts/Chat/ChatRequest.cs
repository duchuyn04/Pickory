namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Yêu cầu chat gửi qua gateway. `MaxSteps` giới hạn số bước tool, còn `MaxTokens` giới hạn token output của mỗi bước.
public sealed record ChatRequest
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }
    public int? MaxSteps { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}
