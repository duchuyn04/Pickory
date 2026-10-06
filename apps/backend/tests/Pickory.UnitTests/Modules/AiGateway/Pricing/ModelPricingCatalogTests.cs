using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Pricing;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Pricing;

public sealed class ModelPricingCatalogTests
{
    [Fact]
    // 1 triệu token miss, 500 nghìn token cache hit, 200 nghìn token output. Tổng là 0,3 + 0,003 + 0,24 USD.
    public void CalculateCostUsd_DeepSeekFlashUsesConservativePeakPrices()
    {
        var usage = new TokenUsage(InputTokens: 1_500_000, OutputTokens: 200_000, CachedTokens: 500_000);

        var cost = ModelPricingCatalog.CalculateCostUsd("deepseek-flash", usage);

        Assert.Equal(0.543, cost, precision: 5);
    }

    [Fact]
    // DeepSeek Pro dùng giá peak chính thức: miss 1,32, hit 0,044, output 3,96 USD cho một triệu token.
    public void CalculateCostUsd_DeepSeekProUsesOfficialPeakPrices()
    {
        var usage = new TokenUsage(InputTokens: 1_500_000, OutputTokens: 200_000, CachedTokens: 500_000);

        var cost = ModelPricingCatalog.CalculateCostUsd("deepseek-v4-pro", usage);

        Assert.Equal(2.134, cost, precision: 5);
    }

    [Fact]
    // OpenAI `text-embedding-3-small` có giá 0,02 USD cho một triệu token.
    public void CalculateCostUsd_OpenAiSmallEmbedding()
    {
        var usage = new TokenUsage(InputTokens: 1_000_000, OutputTokens: 0, CachedTokens: 0);

        var cost = ModelPricingCatalog.CalculateCostUsd("text-embedding-3-small", usage);

        Assert.Equal(0.02, cost, precision: 6);
    }

    [Fact]
    // Chỉ hai model giả có tên cố định được miễn phí. Model lạ không được coi là miễn phí.
    public void CalculateCostUsd_FakeModelsCostNothing()
    {
        Assert.Equal(0, ModelPricingCatalog.CalculateCostUsd("fake-chat", new TokenUsage(10_000, 5_000, 0)));
        Assert.Equal(0, ModelPricingCatalog.CalculateCostUsd("fake-embedding", new TokenUsage(20_000, 0, 0)));
    }

    [Fact]
    // `deepseek-chat` đã ngừng hỗ trợ. Gateway từ chối tính giá thay vì tự gán đơn giá.
    public void CalculateCostUsd_RejectsRetiredOrUnknownModels()
    {
        Assert.Null(ModelPricingCatalog.Find("deepseek-chat"));
        Assert.Throws<InvalidOperationException>(
            () => ModelPricingCatalog.CalculateCostUsd("deepseek-chat", new TokenUsage(100, 50, 0)));
    }

    [Fact]
    // Tên model trong biến môi trường có thể có khoảng trắng hoặc chữ hoa. Bảng giá vẫn nhận đúng model.
    public void Find_NormalizesCaseAndWhitespace()
    {
        Assert.NotNull(ModelPricingCatalog.Find("  DeepSeek-Flash "));
    }
}
