namespace Pickory.Modules.AiGateway.Contracts.Chat;

public sealed record SystemChatMessage(string Content) : ChatMessage(Content);
