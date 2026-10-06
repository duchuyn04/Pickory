using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Model EF Core của AiGateway không được sinh cảnh báo. Cảnh báo lặp lại ở mỗi lần API khởi động làm nhiễu log.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AiGatewayModelTests(IntegrationDatabaseFixture database)
{
    [Fact]
    // Dựng model và ghi một dòng cấu hình. Không có cảnh báo nào về bảng `ai_settings`.
    public async Task Model_HasNoAiSettingWarnings()
    {
        var warnings = new List<string>();
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>();
        AiGatewayDbContext.ConfigureOptions(options, database.ConnectionString);
        options.LogTo(warnings.Add, LogLevel.Warning);
        // Dựng model mới cho test này. Model dùng lại từ test khác đã kiểm tra xong và không ghi cảnh báo nữa.
        options.EnableServiceProviderCaching(false);
        await using var aiGateway = new AiGatewayDbContext(options.Options);

        _ = aiGateway.Model;
        await aiGateway.Settings.AnyAsync();

        Assert.DoesNotContain(warnings, warning => warning.Contains("AiSetting", StringComparison.Ordinal));
    }
}
