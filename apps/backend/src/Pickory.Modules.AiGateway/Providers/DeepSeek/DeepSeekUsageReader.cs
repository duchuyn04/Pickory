using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Pickory.Modules.AiGateway.Contracts.Chat;

namespace Pickory.Modules.AiGateway.Providers.DeepSeek;

// Đọc usage của DeepSeek. Token cache hit nằm ở trường riêng `prompt_cache_hit_tokens` mà SDK OpenAI không đọc.
public static class DeepSeekUsageReader
{
    public static TokenUsage Read(UsageDetails? usage, object? rawRepresentation)
    {
        var cachedTokens = ReadCacheHitTokens(rawRepresentation) ?? usage?.CachedInputTokenCount ?? 0;
        return new TokenUsage(
            (int)(usage?.InputTokenCount ?? 0),
            (int)(usage?.OutputTokenCount ?? 0),
            (int)cachedTokens);
    }

    // Đọc lại JSON gốc của phản hồi. Token cache hit có đơn giá riêng, thiếu trường này làm chi phí sai.
    private static long? ReadCacheHitTokens(object? rawRepresentation)
    {
        var rawJson = rawRepresentation switch
        {
            ChatCompletion completion => ModelReaderWriter.Write(completion).ToString(),
            StreamingChatCompletionUpdate update => ModelReaderWriter.Write(update).ToString(),
            _ => null,
        };
        if (rawJson is null)
        {
            return null;
        }

        var cacheHitTokens = JsonNode.Parse(rawJson)?["usage"]?["prompt_cache_hit_tokens"];
        return cacheHitTokens?.GetValue<long>();
    }
}
