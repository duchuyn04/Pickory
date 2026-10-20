using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// Module AI đã gỡ, nhưng máy chủ cũ có thể còn biến AI trong môi trường hoặc trong `.env`. API phải bỏ qua các biến này.
public sealed class AppConfigLegacyAiVariablesTests
{
    private static AppConfig Load(params (string Key, string Value)[] legacyVariables)
    {
        var values = new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
        };
        foreach (var (key, value) in legacyVariables)
        {
            values[key] = value;
        }

        return AppConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Fact]
    // Đủ biến AI cũ với giá trị hợp lệ. Cấu hình đọc xong, và `DATABASE_URL` vẫn đúng.
    public void Load_IgnoresValidLegacyAiVariables()
    {
        var config = Load(
            ("LLM_BASE_URL", "https://api.deepseek.com"),
            ("LLM_API_KEY", "khoa-bi-mat"),
            ("LLM_MODEL", "deepseek-flash"),
            ("EMBEDDING_API_KEY", "khoa-embedding"),
            ("EMBEDDING_MODEL", "text-embedding-3-small"),
            ("AI_MONTHLY_BUDGET_USD", "30"),
            ("AI_CHAT_DAILY_LIMIT_USER", "50"),
            ("AI_CHAT_DAILY_LIMIT_ANON", "15"),
            ("AI_LIVE_TESTS", "1"));

        Assert.Equal("postgresql://pickory:pickory@localhost:5432/pickory", config.DatabaseUrl);
    }

    [Fact]
    // Giá trị AI cũ từng làm API dừng khởi động (URL sai, có khóa mà thiếu model, số âm, cờ lạ). Nay không còn ai kiểm các giá trị này.
    public void Load_DoesNotRejectInvalidLegacyAiVariables()
    {
        var config = Load(
            ("LLM_BASE_URL", "khong-phai-url"),
            ("LLM_API_KEY", "khoa-khong-co-model"),
            ("AI_MONTHLY_BUDGET_USD", "-1"),
            ("AI_CHAT_DAILY_LIMIT_USER", "1.5"),
            ("AI_CHAT_DAILY_LIMIT_ANON", "0"),
            ("AI_LIVE_TESTS", "true"));

        Assert.Equal("postgresql://pickory:pickory@localhost:5432/pickory", config.DatabaseUrl);
    }
}
