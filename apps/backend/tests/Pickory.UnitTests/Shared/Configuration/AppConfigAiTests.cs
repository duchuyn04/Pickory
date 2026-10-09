using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// Biến môi trường AI chỉ cấp giá trị khởi tạo. Giá trị sai phải dừng API ngay khi khởi động, giống bản Node.
public sealed class AppConfigAiTests
{
    private static AppConfig Load(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return AppConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Fact]
    // Thiếu biến AI thì dùng mặc định của bản Node. Dev chạy được khi chưa có khóa provider.
    public void Load_UsesNodeDefaultsForAi()
    {
        var config = Load();

        Assert.Equal(string.Empty, config.LlmModel);
        Assert.Equal("https://api.deepseek.com", config.LlmBaseUrl);
        Assert.Equal(string.Empty, config.LlmApiKey);
        Assert.Equal(string.Empty, config.EmbeddingApiKey);
        Assert.Equal("text-embedding-3-small", config.EmbeddingModel);
        Assert.Equal(30, config.AiMonthlyBudgetUsd);
        Assert.Equal(50, config.AiChatDailyLimitUser);
        Assert.Equal(15, config.AiChatDailyLimitAnon);
        Assert.False(config.AiLiveTests);
    }

    [Fact]
    // Có khóa chat thì phải chọn model tường minh. Thông báo lỗi không được chứa khóa.
    public void Load_ChatKeyRequiresExplicitModelWithoutLeakingKey()
    {
        const string secret = "private-provider-key";

        var error = Assert.Throws<InvalidOperationException>(() => Load(("LLM_API_KEY", secret)));

        Assert.Contains("LLM_MODEL", error.Message);
        Assert.DoesNotContain(secret, error.ToString());
        Assert.Equal("deepseek-flash", Load(("LLM_API_KEY", secret), ("LLM_MODEL", "deepseek-flash")).LlmModel);
    }

    [Theory]
    [InlineData("AI_MONTHLY_BUDGET_USD", "-1")]
    [InlineData("AI_MONTHLY_BUDGET_USD", "Infinity")]
    [InlineData("AI_CHAT_DAILY_LIMIT_ANON", "0")]
    [InlineData("AI_CHAT_DAILY_LIMIT_USER", "1.5")]
    [InlineData("AI_LIVE_TESTS", "true")]
    // Ngân sách âm, hạn mức không nguyên dương hoặc cờ lạ bị từ chối. Thông báo nêu tên biến sai.
    public void Load_RejectsInvalidAiValues(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load((key, value)));

        Assert.Contains(key, error.Message);
    }

    [Fact]
    // URL provider sai dừng khởi động. API không gửi khóa tới địa chỉ không hợp lệ.
    public void Load_RejectsInvalidLlmBaseUrl()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(("LLM_BASE_URL", "khong-phai-url")));

        Assert.Contains("LLM_BASE_URL", error.Message);
    }

    [Fact]
    // Giá trị hợp lệ từ môi trường ghi đè mặc định. Cờ `AI_LIVE_TESTS=1` bật test với API thật.
    public void Load_ReadsAiValuesFromEnvironment()
    {
        var config = Load(
            ("AI_MONTHLY_BUDGET_USD", "12.5"),
            ("AI_CHAT_DAILY_LIMIT_USER", "40"),
            ("AI_CHAT_DAILY_LIMIT_ANON", "10"),
            ("AI_LIVE_TESTS", "1"),
            ("EMBEDDING_MODEL", "text-embedding-3-large"));

        Assert.Equal(12.5, config.AiMonthlyBudgetUsd);
        Assert.Equal(40, config.AiChatDailyLimitUser);
        Assert.Equal(10, config.AiChatDailyLimitAnon);
        Assert.True(config.AiLiveTests);
        Assert.Equal("text-embedding-3-large", config.EmbeddingModel);
    }
}
