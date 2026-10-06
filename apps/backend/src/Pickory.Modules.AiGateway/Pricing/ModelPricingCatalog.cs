using Pickory.Modules.AiGateway.Contracts.Chat;

namespace Pickory.Modules.AiGateway.Pricing;

// Bảng giá peak bảo thủ của các model đã thẩm định, theo `docs/adr/0002-ai-gateway-provider-findings.md`.
// Chi phí là ước tính, không phải hóa đơn, và đơn giá phải sửa tay khi provider đổi giá.
public static class ModelPricingCatalog
{
    private static readonly ModelPricing Free = new(0, 0, 0);
    private static readonly ModelPricing DeepSeekFlashConservativePeak = new(0.3, 0.006, 1.2);
    private static readonly ModelPricing DeepSeekProPeak = new(1.32, 0.044, 3.96);
    private static readonly ModelPricing OpenAiEmbeddingSmall = new(0.02, 0.02, 0);
    private static readonly ModelPricing OpenAiEmbeddingLarge = new(0.13, 0.13, 0);

    // Chỉ khớp ID chính xác. Model đã ngừng hỗ trợ như `deepseek-chat` không có giá và bị từ chối.
    private static readonly Dictionary<string, ModelPricing> PricingByModel = new(StringComparer.Ordinal)
    {
        // Hai model giả có tên cố định để model lạ không vô tình được miễn phí.
        ["fake-chat"] = Free,
        ["fake-embedding"] = Free,
        ["deepseek-flash"] = DeepSeekFlashConservativePeak,
        ["deepseek-v4-flash"] = DeepSeekFlashConservativePeak,
        ["deepseek-v4-flash-vision-exp"] = DeepSeekFlashConservativePeak,
        ["deepseek-v4-pro"] = DeepSeekProPeak,
        ["text-embedding-3-small"] = OpenAiEmbeddingSmall,
        ["text-embedding-3-large"] = OpenAiEmbeddingLarge,
    };

    // Trả null khi model chưa có giá. Gateway chặn lời gọi trước khi gửi request ra ngoài.
    public static ModelPricing? Find(string model)
    {
        var normalizedModel = model.Trim().ToLowerInvariant();
        return PricingByModel.GetValueOrDefault(normalizedModel);
    }

    public static double CalculateCostUsd(string model, TokenUsage usage)
    {
        // Không tính 0 USD cho model chưa rõ giá. Chi phí sai làm kiểm tra ngân sách mất tác dụng.
        var pricing = Find(model)
            ?? throw new InvalidOperationException(
                $"Không thể tính chi phí cho model \"{model}\": model chưa có bảng giá đã thẩm định.");

        // Tách token đọc từ cache khỏi token input thường. Hai loại token có đơn giá khác nhau.
        var cachedTokens = Math.Max(0, usage.CachedTokens);
        var nonCachedInputTokens = Math.Max(0, usage.InputTokens - cachedTokens);
        var outputTokens = Math.Max(0, usage.OutputTokens);

        var inputCost = nonCachedInputTokens * pricing.InputMissPerMillionUsd / 1_000_000;
        var cacheCost = cachedTokens * pricing.InputHitPerMillionUsd / 1_000_000;
        var outputCost = outputTokens * pricing.OutputPerMillionUsd / 1_000_000;
        return inputCost + cacheCost + outputCost;
    }
}
