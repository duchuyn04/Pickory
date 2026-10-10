using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// SHIP-02: test tích hợp không đọc token GHN của máy chạy test, nên không thể gọi GHN thật.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class GhnConfigurationIsolationTests(IntegrationDatabaseFixture database)
{
    private static readonly string[] GhnVariableNames = ["GHN_TOKEN", "GHN_SHOP_ID", "GHN_WEBHOOK_SECRET"];

    [Fact]
    public void Factory_IgnoresGhnEnvironmentVariablesOfTheTestMachine()
    {
        // Lưu giá trị cũ để trả lại máy chạy test như ban đầu, kể cả khi assertion lỗi.
        var originalValues = GhnVariableNames.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in GhnVariableNames)
            {
                Environment.SetEnvironmentVariable(name, "gia-tri-ro-ri");
            }

            using var factory = new PickoryWebApplicationFactory(database);
            var config = factory.Services.GetRequiredService<AppConfig>();

            Assert.Equal(string.Empty, config.GhnToken);
            Assert.Equal(string.Empty, config.GhnShopId);
            Assert.Equal(string.Empty, config.GhnWebhookSecret);
        }
        finally
        {
            foreach (var (name, value) in originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    [Fact]
    // `AddShippingModule` chọn client theo config gốc, nên máy test có `GHN_TOKEN` sẽ dựng client thật. Factory phải thay bằng bản giả.
    public void Factory_ReplacesGhnClientWithFake_EvenWhenTestMachineHasToken()
    {
        var originalValues = GhnVariableNames.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in GhnVariableNames)
            {
                Environment.SetEnvironmentVariable(name, "gia-tri-ro-ri");
            }

            using var factory = new PickoryWebApplicationFactory(database);

            Assert.IsType<FakeGhnClient>(factory.Services.GetRequiredService<IGhnClient>());
            // Cùng một thể hiện, để test đặt chế độ lỗi trên `GhnClient` thì mã chạy trong API thấy đúng chế độ đó.
            Assert.Same(factory.GhnClient, factory.Services.GetRequiredService<IGhnClient>());
        }
        finally
        {
            foreach (var (name, value) in originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    [Fact]
    public void Factory_WithoutTokenOnMachine_AlsoUsesFakeGhnClient()
    {
        using var factory = new PickoryWebApplicationFactory(database);

        Assert.IsType<FakeGhnClient>(factory.Services.GetRequiredService<IGhnClient>());
    }
}
