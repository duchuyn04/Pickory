namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Kết quả của một lời gọi chat. `Usage` cộng token của mọi bước trong vòng lặp tool.
public sealed record ChatResult(string Text, TokenUsage Usage, IReadOnlyList<ToolCall>? ToolCalls = null);
