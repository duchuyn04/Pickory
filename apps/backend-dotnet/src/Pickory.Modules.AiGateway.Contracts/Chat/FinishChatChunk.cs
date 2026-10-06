namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Phần cuối của stream. Phần này mang tổng token và các yêu cầu gọi tool của bước cuối.
public sealed record FinishChatChunk(TokenUsage Usage, IReadOnlyList<ToolCall>? ToolCalls = null) : ChatChunk;
