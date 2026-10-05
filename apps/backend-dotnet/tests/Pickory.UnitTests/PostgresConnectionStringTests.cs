using Npgsql;
using Pickory.Shared.Persistence;
using Xunit;

namespace Pickory.UnitTests;

public class PostgresConnectionStringTests
{
    [Theory]
    [InlineData("postgresql://pickory:plain@localhost:5432/pickory", "pickory", "plain", "localhost", 5432, "pickory")]
    [InlineData("postgres://user%40example:p%40ss%3A%2F%3F%23%25%2B%3B%3D%22@localhost/db%20test", "user@example", "p@ss:/?#%+;=\"", "localhost", 5432, "db test")]
    [InlineData("postgresql://user:p%252Fword@[::1]:5433/test", "user", "p%2Fword", "::1", 5433, "test")]
    public void Convert_PreservesDecodedCredentialsAndAddress(string databaseUrl, string username, string password, string host, int port, string database)
    {
        // Đọc lại bằng Npgsql để phát hiện ký tự đặc biệt làm thay đổi cấu trúc chuỗi kết nối.
        var connection = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(databaseUrl));

        Assert.Equal(username, connection.Username);
        Assert.Equal(password, connection.Password);
        Assert.Equal(host, connection.Host);
        Assert.Equal(port, connection.Port);
        Assert.Equal(database, connection.Database);
        Assert.Equal("UTC", connection.Timezone);
    }

    [Fact]
    public void Convert_PreservesSupportedUriOptions()
    {
        // Giữ yêu cầu TLS (Transport Layer Security) từ URL để không hạ mức bảo vệ kết nối.
        var connection = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(
            "postgresql://user:pass@host/db?sslmode=require&connect_timeout=7&application_name=pickory%20test&schema=public"));

        Assert.Equal(SslMode.Require, connection.SslMode);
        Assert.Equal(7, connection.Timeout);
        Assert.Equal("pickory test", connection.ApplicationName);
        Assert.Equal("public", connection.SearchPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://user:pass@host/db")]
    [InlineData("postgresql://user:pass@host/")]
    [InlineData("postgresql://user:pass@host/db?unsupported=value")]
    [InlineData("postgresql://user:pass@host/db#fragment")]
    [InlineData("postgresql://user:pass@host/db?sslmode=invalid")]
    public void Convert_RejectsUnsupportedConnectionWithoutLeakingPassword(string databaseUrl)
    {
        // Không đưa URL vào lỗi vì URL có thể chứa mật khẩu.
        var exception = Assert.Throws<ArgumentException>(() => PostgresConnectionString.Convert(databaseUrl));

        Assert.DoesNotContain("user:pass", exception.Message);
    }
}
