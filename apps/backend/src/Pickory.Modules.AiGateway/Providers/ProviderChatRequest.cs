using Pickory.Modules.AiGateway.Contracts.Chat;

namespace Pickory.Modules.AiGateway.Providers;

// Request của một bước gửi tới adapter, sau khi gateway đã ẩn PII trong tin nhắn và mô tả tool.
// `Json` bật chế độ trả JSON của provider cho `ChatJsonAsync`.
public sealed record ProviderChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools,
    double? Temperature,
    int? MaxTokens,
    bool Json);
