using Microsoft.Extensions.Configuration;

namespace Pickory.Shared.Configuration;

// Quản lý toàn bộ thông số cấu hình khởi động đọc từ biến môi trường.
public record AppConfig
{
    // Cổng mạng cho ứng dụng web lắng nghe.
    public int Port { get; init; } = 3000;

    // Địa chỉ nguồn của giao diện web được phép kết nối.
    public string WebOrigin { get; init; } = "http://localhost:5173";

    // Địa chỉ gốc của hệ thống API.
    public string ApiBaseUrl { get; init; } = "http://localhost:3000";

    // Chuỗi kết nối tới cơ sở dữ liệu PostgreSQL.
    public string DatabaseUrl { get; init; } = string.Empty;

    // Cấu hình tin cậy proxy trung gian cho request chuyển tiếp.
    public string TrustProxy { get; init; } = "false";

    // Đọc cấu hình từ IConfiguration của ứng dụng.
    public static AppConfig Load(IConfiguration configuration)
    {
        var rawPort = configuration["PORT"];
        var port = 3000;
        if (!string.IsNullOrWhiteSpace(rawPort) && int.TryParse(rawPort, out var parsedPort))
        {
            port = parsedPort;
        }

        var webOrigin = configuration["WEB_ORIGIN"];
        if (string.IsNullOrWhiteSpace(webOrigin))
        {
            webOrigin = "http://localhost:5173";
        }

        var apiBaseUrl = configuration["API_BASE_URL"];
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            apiBaseUrl = "http://localhost:3000";
        }

        var databaseUrl = configuration["DATABASE_URL"];

        // Ngăn chặn khởi động ứng dụng khi thiếu cấu hình cơ sở dữ liệu.
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            throw new InvalidOperationException("Thiếu biến môi trường DATABASE_URL bắt buộc.");
        }

        var trustProxy = configuration["TRUST_PROXY"];
        if (string.IsNullOrWhiteSpace(trustProxy))
        {
            trustProxy = "false";
        }

        return new AppConfig
        {
            Port = port,
            WebOrigin = webOrigin,
            ApiBaseUrl = apiBaseUrl,
            DatabaseUrl = databaseUrl,
            TrustProxy = trustProxy,
        };
    }
}
