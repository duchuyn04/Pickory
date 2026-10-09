namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Kết quả của một tool. `ToolCallId` nối kết quả với đúng yêu cầu gọi tool của model.
public sealed record ToolChatMessage(string ToolCallId, string ToolName, string Content) : ChatMessage(Content);
