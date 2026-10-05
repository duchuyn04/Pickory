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

    // Giá trị giả mặc định giữ nguyên bản Node. Môi trường dev chạy được khi chưa có ứng dụng Google thật.
    public string GoogleClientId { get; init; } = "mock_google_client_id";

    public string GoogleClientSecret { get; init; } = "mock_google_client_secret";

    // Đọc và kiểm tra tính hợp lệ của toàn bộ biến môi trường khi khởi động.
    public static AppConfig Load(IConfiguration configuration)
    {
        var rawPort = configuration["PORT"];
        var port = 3000;
        if (!string.IsNullOrWhiteSpace(rawPort))
        {
            // Bắt buộc cổng mạng phải là số nguyên dương lớn hơn không.
            if (!int.TryParse(rawPort, out var parsedPort) || parsedPort <= 0)
            {
                throw new InvalidOperationException("Biến môi trường PORT phải là số nguyên dương hợp lệ.");
            }

            port = parsedPort;
        }

        var webOrigin = configuration["WEB_ORIGIN"];
        if (string.IsNullOrWhiteSpace(webOrigin))
        {
            webOrigin = "http://localhost:5173";
        }
        else if (!IsValidAbsoluteUrl(webOrigin))
        {
            // Từ chối giá trị nguồn web không phải là địa chỉ URL tuyệt đối.
            throw new InvalidOperationException("Biến môi trường WEB_ORIGIN phải là một URL tuyệt đối hợp lệ.");
        }

        var apiBaseUrl = configuration["API_BASE_URL"];
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            apiBaseUrl = "http://localhost:3000";
        }
        else if (!IsValidAbsoluteUrl(apiBaseUrl))
        {
            // Từ chối giá trị địa chỉ gốc API không phải là URL tuyệt đối.
            throw new InvalidOperationException("Biến môi trường API_BASE_URL phải là một URL tuyệt đối hợp lệ.");
        }

        var databaseUrl = configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            // Dừng khởi động ứng dụng khi thiếu chuỗi kết nối cơ sở dữ liệu.
            throw new InvalidOperationException("Thiếu biến môi trường DATABASE_URL bắt buộc.");
        }

        var rawTrustProxy = configuration["TRUST_PROXY"];
        var trustProxy = "false";
        if (!string.IsNullOrWhiteSpace(rawTrustProxy))
        {
            var trimmed = rawTrustProxy.Trim();

            // Chặn giá trị true để tránh tin cậy toàn bộ header chuyển tiếp giả mạo.
            if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Biến môi trường TRUST_PROXY không được nhận giá trị 'true'.");
            }

            if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) || trimmed == "0")
            {
                trustProxy = "false";
            }
            else if (int.TryParse(trimmed, out var hops))
            {
                // Yêu cầu số lượng hop ủy thác phải là số nguyên dương.
                if (hops <= 0)
                {
                    throw new InvalidOperationException("Số hop trong TRUST_PROXY phải là số nguyên dương.");
                }

                trustProxy = hops.ToString();
            }
            else
            {
                // Tách danh sách địa chỉ IP hoặc mạng con được ủy quyền tin cậy.
                var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                trustProxy = parts.Length > 0 ? string.Join(",", parts) : "false";
            }
        }

        // Thiếu biến thì dùng giá trị giả của bản Node. Đăng nhập Google chỉ chạy thật khi có ứng dụng Google.
        var googleClientId = configuration["GOOGLE_CLIENT_ID"];
        var googleClientSecret = configuration["GOOGLE_CLIENT_SECRET"];

        return new AppConfig
        {
            Port = port,
            WebOrigin = webOrigin,
            ApiBaseUrl = apiBaseUrl,
            DatabaseUrl = databaseUrl,
            TrustProxy = trustProxy,
            GoogleClientId = string.IsNullOrWhiteSpace(googleClientId) ? "mock_google_client_id" : googleClientId,
            GoogleClientSecret = string.IsNullOrWhiteSpace(googleClientSecret) ? "mock_google_client_secret" : googleClientSecret,
        };
    }

    private static bool IsValidAbsoluteUrl(string url)
    {
        // Xác minh chuỗi cung cấp thỏa mãn định dạng URL tuyệt đối với giao thức http hoặc https.
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
