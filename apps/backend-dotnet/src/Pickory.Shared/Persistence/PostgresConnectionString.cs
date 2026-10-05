using System.Globalization;
using Npgsql;

namespace Pickory.Shared.Persistence;

public static class PostgresConnectionString
{
    public static string Convert(string databaseUrl)
    {
        // Từ chối URL thiếu đích kết nối. Thông báo không chứa thông tin đăng nhập.
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgresql" && uri.Scheme != "postgres")
            || string.IsNullOrEmpty(uri.Host)
            || uri.AbsolutePath.Length <= 1
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw InvalidDatabaseUrl();
        }

        // Tách trước khi giải mã để dấu hai chấm trong mật khẩu không thành dấu phân cách.
        var credentials = uri.UserInfo.Split(':', 2);
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host.Trim('[', ']'),
            Port = uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length == 2 ? Uri.UnescapeDataString(credentials[1]) : string.Empty,
            // Giữ `CURRENT_TIMESTAMP` theo UTC (Coordinated Universal Time) vì DB dùng cột không múi giờ.
            Timezone = "UTC",
        };

        // Chỉ chuyển tùy chọn có nghĩa tương đương. Không bỏ qua tùy chọn bảo mật không hỗ trợ.
        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = parameter.Split('=', 2);
            var name = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            switch (name)
            {
                case "schema":
                    connection.SearchPath = value;
                    break;
                case "application_name":
                    connection.ApplicationName = value;
                    break;
                case "sslmode":
                    // Giữ yêu cầu TLS (Transport Layer Security) từ URL để không hạ mức bảo vệ kết nối.
                    if (!Enum.TryParse<SslMode>(value.Replace("-", string.Empty), true, out var sslMode)
                        || !Enum.IsDefined(sslMode))
                    {
                        throw InvalidDatabaseUrl();
                    }
                    connection.SslMode = sslMode;
                    break;
                case "connect_timeout":
                    // Giữ thời hạn hợp lệ để Npgsql không nhận số âm hoặc giá trị không phải số.
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var timeout))
                    {
                        throw InvalidDatabaseUrl();
                    }
                    connection.Timeout = timeout;
                    break;
                default:
                    throw InvalidDatabaseUrl();
            }
        }

        // Dùng bộ dựng của Npgsql để mật khẩu không thể chèn thêm tùy chọn kết nối.
        return connection.ConnectionString;
    }

    private static ArgumentException InvalidDatabaseUrl()
    {
        return new ArgumentException("DATABASE_URL phải là URI PostgreSQL hợp lệ với tùy chọn được hỗ trợ.", "databaseUrl");
    }
}
