using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Shared.Privacy;

namespace Pickory.Modules.AiGateway.Gateway;

// Ẩn PII trong mọi phần của hội thoại trước khi gửi cho provider.
public static class MessageRedactor
{
    public static List<ChatMessage> RedactMessages(IEnumerable<ChatMessage> messages)
    {
        return messages.Select(RedactMessage).ToList();
    }

    // Mảng tool rỗng gửi đi như không có tool. Provider không nhận danh sách tool trống.
    public static IReadOnlyList<ToolDefinition>? RedactTools(IReadOnlyList<ToolDefinition>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return null;
        }

        return tools.Select(tool => (ToolDefinition)new RedactedToolDefinition(tool)).ToList();
    }

    private static ChatMessage RedactMessage(ChatMessage message)
    {
        var content = PiiRedactor.Redact(message.Content);
        return message switch
        {
            // Input của tool là cây JSON. Ẩn theo cây giữ JSON hợp lệ khi số nhạy cảm đổi thành chuỗi.
            AssistantChatMessage { ToolCalls: { } toolCalls } => new AssistantChatMessage(
                content,
                toolCalls.Select(call => call with { Input = PiiRedactor.RedactValue(call.Input) }).ToList()),
            AssistantChatMessage => new AssistantChatMessage(content),
            ToolChatMessage toolMessage => toolMessage with { Content = content },
            SystemChatMessage => new SystemChatMessage(content),
            UserChatMessage => new UserChatMessage(content),
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Loại tin nhắn không được hỗ trợ."),
        };
    }
}
