namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Câu trả lời của model. `ToolCalls` giữ các yêu cầu gọi tool của bước đó để model thấy lại ở bước sau.
public sealed record AssistantChatMessage(string Content, IReadOnlyList<ToolCall>? ToolCalls = null)
    : ChatMessage(Content);
