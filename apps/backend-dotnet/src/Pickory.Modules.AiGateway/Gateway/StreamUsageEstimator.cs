using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Providers;

namespace Pickory.Modules.AiGateway.Gateway;

// Ước tính token khi stream đứt hoặc bị hủy sau khi đã có text nhưng thiếu usage của provider.
// Số byte UTF-8 lớn hơn số token, nên ước tính này nghiêng về phía an toàn cho ngân sách.
public static class StreamUsageEstimator
{
    public static TokenUsage Estimate(ProviderChatRequest request, string receivedText)
    {
        var requestJson = new JsonObject
        {
            ["messages"] = new JsonArray(request.Messages.Select(ToJson).ToArray()),
            ["tools"] = request.Tools is null ? null : new JsonArray(request.Tools.Select(ToJson).ToArray()),
        };
        var inputBytes = Encoding.UTF8.GetByteCount(requestJson.ToJsonString(PromptJson.Options));
        var outputBytes = Encoding.UTF8.GetByteCount(receivedText);
        return new TokenUsage(inputBytes, outputBytes, 0);
    }

    private static JsonNode ToJson(ChatMessage message)
    {
        return message switch
        {
            SystemChatMessage => new JsonObject { ["role"] = "system", ["content"] = message.Content },
            UserChatMessage => new JsonObject { ["role"] = "user", ["content"] = message.Content },
            AssistantChatMessage assistant => new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = assistant.Content,
                ["toolCalls"] = assistant.ToolCalls is null
                    ? null
                    : new JsonArray(assistant.ToolCalls.Select(call => (JsonNode)new JsonObject
                    {
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["input"] = call.Input?.DeepClone(),
                    }).ToArray()),
            },
            ToolChatMessage tool => new JsonObject
            {
                ["role"] = "tool",
                ["toolCallId"] = tool.ToolCallId,
                ["toolName"] = tool.ToolName,
                ["content"] = tool.Content,
            },
            _ => new JsonObject { ["content"] = message.Content },
        };
    }

    // Schema của tham số cũng được gửi cho provider, nên được tính vào input.
    private static JsonNode ToJson(ToolDefinition tool)
    {
        return new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = PromptJson.Options.GetJsonSchemaAsNode(tool.ParametersType),
        };
    }
}
