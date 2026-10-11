using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// COM-03: test tích hợp không đọc khóa SePay của máy chạy test, nên webhook thật không bao giờ được xác thực nhầm.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class SepayConfigurationIsolationTests(IntegrationDatabaseFixture database)
{
    [Fact]
    public void Factory_IgnoresSepayEnvironmentVariableOfTheTestMachine()
    {
        // Lưu giá trị cũ để trả lại máy chạy test như ban đầu, kể cả khi assertion lỗi.
        var originalValue = Environment.GetEnvironmentVariable("SEPAY_WEBHOOK_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("SEPAY_WEBHOOK_API_KEY", "gia-tri-ro-ri");

            using var factory = new PickoryWebApplicationFactory(database);
            var config = factory.Services.GetRequiredService<AppConfig>();

            Assert.Equal(string.Empty, config.SepayWebhookApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SEPAY_WEBHOOK_API_KEY", originalValue);
        }
    }
}
