namespace Pickory.Modules.AiGateway.Contracts.Chat;

public sealed record UserChatMessage(string Content) : ChatMessage(Content);
