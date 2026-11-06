using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// COM-03: test tích hợp không đọc khóa SePay của máy chạy test, nên webhook thật không bao giờ được xác thực nhầm.
[Collection(ApiCollection.Name)]
public sealed class SepayConfigurationIsolationTests(ApiDatabaseFixture database)
{
    [Fact]
    public void Factory_IgnoresSepayEnvironmentVariableOfTheTestMachine()
    {
        // Đồng bộ hóa để không gây race condition biến môi trường với các collection song song khác.
        lock (ProcessEnvironmentLock.Gate)
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
}
