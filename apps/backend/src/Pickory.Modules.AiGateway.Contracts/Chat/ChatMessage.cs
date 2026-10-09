namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Một tin nhắn trong hội thoại gửi tới model. Gateway ẩn PII trong `Content` trước khi gửi.
public abstract record ChatMessage(string Content);
