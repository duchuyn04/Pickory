using Microsoft.Extensions.Configuration;
using Npgsql;
using Pickory.Shared.Configuration;
using Pickory.Shared.Persistence;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// `DATABASE_POOL_MAX` giới hạn số kết nối DB của API. Supabase Session pooler gói free chỉ cho 15 kết nối cùng lúc.
public sealed class AppConfigDatabasePoolTests
{
    private const string DatabaseUrl = "postgresql://pickory:pickory@localhost:5432/pickory";

    private static AppConfig Load(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?> { ["DATABASE_URL"] = DatabaseUrl };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return AppConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Fact]
    // Không đặt biến thì giữ mặc định của Npgsql, để dev và test không đổi hành vi.
    public void Load_WithoutPoolVariable_LeavesPoolLimitUnset()
    {
        Assert.Null(Load().DatabasePoolMax);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    // Biến rỗng là cách tắt giới hạn trong `.env.example`, nên không được coi là lỗi.
    public void Load_WithEmptyPoolVariable_LeavesPoolLimitUnset(string rawValue)
    {
        Assert.Null(Load(("DATABASE_POOL_MAX", rawValue)).DatabasePoolMax);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("10", 10)]
    [InlineData("100", 100)]
    public void Load_ReadsPoolLimitInsideTheAllowedRange(string rawValue, int expected)
    {
        Assert.Equal(expected, Load(("DATABASE_POOL_MAX", rawValue)).DatabasePoolMax);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("10.5")]
    // Giá trị ngoài khoảng 1 đến 100 làm API dừng khi khởi động. Thông báo nêu tên biến.
    public void Load_WithInvalidPoolLimit_ThrowsAndNamesTheVariable(string rawValue)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(("DATABASE_POOL_MAX", rawValue)));

        Assert.Contains("DATABASE_POOL_MAX", exception.Message);
    }

    [Fact]
    public void WithMaxPoolSize_SetsTheLimitOnTheConnectionString()
    {
        var converted = PostgresConnectionString.Convert(DatabaseUrl);

        var limited = PostgresConnectionString.WithMaxPoolSize(converted, 7);

        Assert.Equal(7, new NpgsqlConnectionStringBuilder(limited).MaxPoolSize);
    }

    [Fact]
    // Không đặt giới hạn thì chuỗi kết nối giữ nguyên, kể cả mặc định `Maximum Pool Size` của Npgsql.
    public void WithMaxPoolSize_WithoutLimit_ReturnsTheSameString()
    {
        var converted = PostgresConnectionString.Convert(DatabaseUrl);

        Assert.Equal(converted, PostgresConnectionString.WithMaxPoolSize(converted, null));
    }
}
