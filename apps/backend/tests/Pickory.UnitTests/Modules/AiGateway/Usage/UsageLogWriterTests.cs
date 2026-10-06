using Microsoft.EntityFrameworkCore;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.AiGateway.Usage;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Usage;

public sealed class UsageLogWriterTests
{
    private const string Password = "secret-db-password";

    [Fact]
    // Actor được lưu dưới dạng SHA-256 hex. Bảng log đối soát được theo người mà không lưu ID gốc.
    public void HashActorKey_ReturnsSha256HexOfTrimmedKey()
    {
        Assert.Equal(
            "2bb80d537b1da3e38bd30361aa855686bde0eacd7162fef6a25fe97bf527a25b",
            UsageLogWriter.HashActorKey(" secret "));
        Assert.Null(UsageLogWriter.HashActorKey(null));
        Assert.Null(UsageLogWriter.HashActorKey("   "));
    }

    [Fact]
    // DB lỗi khi ghi log. Exception và log không được chứa chuỗi kết nối hay mật khẩu DB.
    public async Task WriteAsync_DatabaseFailureDoesNotLeakConnectionString()
    {
        var connectionString = $"Host=127.0.0.1;Port=1;Database=pickory;Username=pickory;Password={Password};Timeout=1";
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>();
        AiGatewayDbContext.ConfigureOptions(options, connectionString);
        var logger = new RecordingLogger<UsageLogWriter>();
        var writer = new UsageLogWriter(new FixedDbContextFactory(options.Options), TimeProvider.System, logger);
        var entry = new UsageLogEntry(AiFeature.ShoppingAssistantChat, "fake", "fake-chat", UsageStatus.Ok);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(entry, CancellationToken.None));

        Assert.Equal("Không thể lưu nhật ký sử dụng AI.", error.Message);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.DoesNotContain(logger.RecordedMessages, message => message.Contains(Password, StringComparison.Ordinal));
        Assert.Contains(Microsoft.Extensions.Logging.LogLevel.Error, logger.RecordedLevels);
    }

    // Factory trả context mới mỗi lần. Writer tạo context riêng cho mỗi dòng log.
    private sealed class FixedDbContextFactory(DbContextOptions<AiGatewayDbContext> options)
        : IDbContextFactory<AiGatewayDbContext>
    {
        public AiGatewayDbContext CreateDbContext()
        {
            return new AiGatewayDbContext(options);
        }
    }
}
