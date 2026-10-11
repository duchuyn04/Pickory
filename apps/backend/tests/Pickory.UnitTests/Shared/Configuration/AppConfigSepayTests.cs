using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

public sealed class AppConfigSepayTests
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
    // Dev và test không đặt biến vẫn chạy, nên khóa mặc định là chuỗi rỗng.
    public void Load_WithoutSepayKey_DefaultsToEmpty()
    {
        Assert.Equal(string.Empty, Load().SepayWebhookApiKey);
    }

    [Fact]
    // Khoảng trắng đầu và cuối do sao chép biến môi trường bị cắt, để so khóa với header không lệch vì khoảng trắng.
    public void Load_WithSepayKey_ReadsTrimmedValue()
    {
        var config = Load(("SEPAY_WEBHOOK_API_KEY", "  khoa-bi-mat  "));

        Assert.Equal("khoa-bi-mat", config.SepayWebhookApiKey);
    }
}
