using System.Globalization;
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

    // Không có model chat mặc định. Người vận hành phải xác nhận ID model trước khi dùng khóa thật.
    public string LlmModel { get; init; } = string.Empty;

    public string LlmBaseUrl { get; init; } = "https://api.deepseek.com";

    public string LlmApiKey { get; init; } = string.Empty;

    public string EmbeddingApiKey { get; init; } = string.Empty;

    public string EmbeddingModel { get; init; } = "text-embedding-3-small";

    // Ba giá trị sau chỉ khởi tạo bảng `ai_settings`. Thay đổi sau đó lưu trong DB.
    public double AiMonthlyBudgetUsd { get; init; } = 30;

    public int AiChatDailyLimitUser { get; init; } = 50;

    public int AiChatDailyLimitAnon { get; init; } = 15;

    // Bật test gọi API AI thật. Test thường không gọi provider thật để không tốn tiền.
    public bool AiLiveTests { get; init; }

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

        var llmBaseUrl = configuration["LLM_BASE_URL"];
        if (string.IsNullOrWhiteSpace(llmBaseUrl))
        {
            llmBaseUrl = "https://api.deepseek.com";
        }
        else if (!IsValidAbsoluteUrl(llmBaseUrl))
        {
            // API không gửi khóa provider tới địa chỉ không hợp lệ.
            throw new InvalidOperationException("Biến môi trường LLM_BASE_URL phải là một URL tuyệt đối hợp lệ.");
        }

        var llmApiKey = configuration["LLM_API_KEY"] ?? string.Empty;
        var llmModel = (configuration["LLM_MODEL"] ?? string.Empty).Trim();
        // Có khóa mà thiếu model thì dừng khởi động. Thông báo không chứa khóa để khóa không lọt vào log.
        if (!string.IsNullOrEmpty(llmApiKey) && llmModel.Length == 0)
        {
            throw new InvalidOperationException("LLM_MODEL bắt buộc khi có LLM_API_KEY.");
        }

        var embeddingModel = configuration["EMBEDDING_MODEL"];

        return new AppConfig
        {
            Port = port,
            WebOrigin = webOrigin,
            ApiBaseUrl = apiBaseUrl,
            DatabaseUrl = databaseUrl,
            TrustProxy = trustProxy,
            GoogleClientId = string.IsNullOrWhiteSpace(googleClientId) ? "mock_google_client_id" : googleClientId,
            GoogleClientSecret = string.IsNullOrWhiteSpace(googleClientSecret) ? "mock_google_client_secret" : googleClientSecret,
            LlmModel = llmModel,
            LlmBaseUrl = llmBaseUrl,
            LlmApiKey = llmApiKey,
            EmbeddingApiKey = configuration["EMBEDDING_API_KEY"] ?? string.Empty,
            EmbeddingModel = string.IsNullOrWhiteSpace(embeddingModel) ? "text-embedding-3-small" : embeddingModel,
            AiMonthlyBudgetUsd = ReadNonNegativeNumber(configuration, "AI_MONTHLY_BUDGET_USD", 30),
            AiChatDailyLimitUser = ReadPositiveInteger(configuration, "AI_CHAT_DAILY_LIMIT_USER", 50),
            AiChatDailyLimitAnon = ReadPositiveInteger(configuration, "AI_CHAT_DAILY_LIMIT_ANON", 15),
            AiLiveTests = ReadLiveTestsFlag(configuration),
        };
    }

    private static double ReadNonNegativeNumber(IConfiguration configuration, string key, double defaultValue)
    {
        var rawValue = configuration[key];
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        // Ngân sách âm hoặc vô hạn làm phép chia tỉ lệ ngân sách sai, nên API dừng khởi động.
        var isValid = double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value)
            && value >= 0;
        if (!isValid)
        {
            throw new InvalidOperationException($"Biến môi trường {key} phải là số hữu hạn không âm.");
        }

        return value;
    }

    private static int ReadPositiveInteger(IConfiguration configuration, string key, int defaultValue)
    {
        var rawValue = configuration[key];
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        // Hạn mức là số lượt chat. Giá trị lẻ hoặc bằng 0 không có nghĩa, nên API dừng khởi động.
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new InvalidOperationException($"Biến môi trường {key} phải là số nguyên dương.");
        }

        return value;
    }

    private static bool ReadLiveTestsFlag(IConfiguration configuration)
    {
        var rawValue = configuration["AI_LIVE_TESTS"];
        if (string.IsNullOrWhiteSpace(rawValue) || rawValue == "0")
        {
            return false;
        }

        // Chỉ nhận `1`, giống bản Node. Giá trị như `true` bị từ chối để không bật test tốn tiền do gõ nhầm.
        if (rawValue == "1")
        {
            return true;
        }

        throw new InvalidOperationException("Biến môi trường AI_LIVE_TESTS chỉ nhận giá trị 0 hoặc 1.");
    }

    private static bool IsValidAbsoluteUrl(string url)
    {
        // Xác minh chuỗi cung cấp thỏa mãn định dạng URL tuyệt đối với giao thức http hoặc https.
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
