using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Pickory.Modules.AiGateway.Contracts.Chat;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using PickoryChatMessage = Pickory.Modules.AiGateway.Contracts.Chat.ChatMessage;

namespace Pickory.Modules.AiGateway.Providers.DeepSeek;

// Đổi tin nhắn giữa kiểu của Pickory và kiểu của `Microsoft.Extensions.AI`.
public static class DeepSeekMessageConverter
{
    public static List<AiChatMessage> ToAiMessages(IReadOnlyList<PickoryChatMessage> messages)
    {
        return messages.Select(ToAiMessage).ToList();
    }

    public static ToolCall ToToolCall(FunctionCallContent call)
    {
        // Giữ đối số thô của model. Gateway tự kiểm tra kiểu và báo lỗi cho model nếu sai.
        var input = call.Arguments is null ? null : JsonSerializer.SerializeToNode(call.Arguments);
        return new ToolCall(call.CallId, call.Name, input);
    }

    private static AiChatMessage ToAiMessage(PickoryChatMessage message)
    {
        return message switch
        {
            SystemChatMessage => new AiChatMessage(ChatRole.System, message.Content),
            UserChatMessage => new AiChatMessage(ChatRole.User, message.Content),
            AssistantChatMessage assistant => ToAssistantMessage(assistant),
            // Kết quả tool gắn với đúng yêu cầu gọi tool qua `ToolCallId`.
            ToolChatMessage tool => new AiChatMessage(ChatRole.Tool, [new FunctionResultContent(tool.ToolCallId, tool.Content)]),
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Loại tin nhắn không được hỗ trợ."),
        };
    }

    private static AiChatMessage ToAssistantMessage(AssistantChatMessage assistant)
    {
        var contents = new List<AIContent>();
        // Bỏ text rỗng. Provider nhận tin nhắn chỉ có yêu cầu gọi tool.
        if (assistant.Content.Length > 0)
        {
            contents.Add(new TextContent(assistant.Content));
        }

        foreach (var call in assistant.ToolCalls ?? [])
        {
            contents.Add(new FunctionCallContent(call.Id, call.Name, ToArguments(call.Input)));
        }

        return new AiChatMessage(ChatRole.Assistant, contents);
    }

    // Đối số phải là object JSON. Input khác object được gửi như đối số rỗng.
    private static Dictionary<string, object?> ToArguments(JsonNode? input)
    {
        if (input is not JsonObject jsonObject)
        {
            return [];
        }

        return jsonObject.ToDictionary(
            pair => pair.Key,
            pair => (object?)(pair.Value is null ? null : JsonSerializer.SerializeToElement(pair.Value)));
    }
}
