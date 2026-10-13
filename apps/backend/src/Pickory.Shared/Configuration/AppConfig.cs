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

    // Số kết nối tối đa mà API mở tới DB. Null nghĩa là giữ mặc định của Npgsql (100 kết nối).
    public int? DatabasePoolMax { get; init; }

    // Số giây cache danh sách sản phẩm và cây danh mục công khai. 0 nghĩa là tắt cache.
    public int CatalogCacheSeconds { get; init; }

    // Đường dẫn tới `index.html` đã build của SPA, nơi API chèn thẻ meta SEO cho `/p/{slug}` và `/c/{slug}`. Rỗng nghĩa là tắt.
    public string WebIndexHtmlPath { get; init; } = string.Empty;

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

    // Khóa Cloudinary để ký yêu cầu tải ảnh và xóa ảnh. Chuỗi rỗng nghĩa là chưa cấu hình.
    public string CloudinaryCloudName { get; init; } = string.Empty;

    public string CloudinaryApiKey { get; init; } = string.Empty;

    public string CloudinaryApiSecret { get; init; } = string.Empty;

    // Địa chỉ gốc của API GHN. Mặc định trỏ tới sandbox để dev và test không gọi nhầm production.
    public string GhnApiBase { get; init; } = "https://dev-online-gateway.ghn.vn";

    // Ba giá trị bí mật của GHN. Chuỗi rỗng nghĩa là chưa cấu hình, và production thiếu một giá trị thì API dừng.
    public string GhnToken { get; init; } = string.Empty;

    public string GhnShopId { get; init; } = string.Empty;

    public string GhnWebhookSecret { get; init; } = string.Empty;

    // Bật test gọi sandbox GHN thật. Test thường không gọi GHN.
    public bool GhnLiveTests { get; init; }

    // Bật tài liệu API chỉ xem ở môi trường khác Development. Mặc định tắt, vì tài liệu liệt kê cả route admin.
    public bool ApiDocsEnabled { get; init; }

    // Cho gọi thử API từ trang tài liệu ở môi trường khác Development. Mặc định tắt, và chỉ có tác dụng khi `ApiDocsEnabled` bật.
    public bool ApiDocsInteractive { get; init; }

    // Có đủ bộ ba khóa thì dùng Cloudinary thật. `Load` đã chặn trường hợp chỉ có một phần.
    public bool HasCloudinary => CloudinaryCloudName.Length > 0;

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

        var cloudinaryCloudName = (configuration["CLOUDINARY_CLOUD_NAME"] ?? string.Empty).Trim();
        var cloudinaryApiKey = (configuration["CLOUDINARY_API_KEY"] ?? string.Empty).Trim();
        var cloudinaryApiSecret = (configuration["CLOUDINARY_API_SECRET"] ?? string.Empty).Trim();
        var cloudinaryKeyCount = new[] { cloudinaryCloudName, cloudinaryApiKey, cloudinaryApiSecret }
            .Count(value => value.Length > 0);
        // Thiếu một khóa thì ảnh tải lên được nhưng không xóa được. Thông báo không chứa giá trị khóa để secret không lọt vào log.
        if (cloudinaryKeyCount is > 0 and < 3)
        {
            throw new InvalidOperationException(
                "Cần đủ CLOUDINARY_CLOUD_NAME, CLOUDINARY_API_KEY và CLOUDINARY_API_SECRET, hoặc bỏ trống cả ba.");
        }

        var ghnApiBase = (configuration["GHN_API_BASE"] ?? string.Empty).Trim();
        if (ghnApiBase.Length == 0)
        {
            ghnApiBase = "https://dev-online-gateway.ghn.vn";
        }
        else if (!IsValidAbsoluteUrl(ghnApiBase))
        {
            // API không gửi token GHN tới địa chỉ không hợp lệ.
            throw new InvalidOperationException("Biến môi trường GHN_API_BASE phải là một URL tuyệt đối hợp lệ.");
        }

        return new AppConfig
        {
            Port = port,
            WebOrigin = webOrigin,
            ApiBaseUrl = apiBaseUrl,
            DatabaseUrl = databaseUrl,
            DatabasePoolMax = ReadOptionalIntegerInRange(configuration, "DATABASE_POOL_MAX", 1, 100),
            // Không đặt biến thì tắt cache. Giá trị ngoài khoảng 0 đến 300 làm API dừng khi khởi động.
            CatalogCacheSeconds = ReadOptionalIntegerInRange(configuration, "CATALOG_CACHE_SECONDS", 0, 300) ?? 0,
            WebIndexHtmlPath = (configuration["WEB_INDEX_HTML_PATH"] ?? string.Empty).Trim(),
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
            CloudinaryCloudName = cloudinaryCloudName,
            CloudinaryApiKey = cloudinaryApiKey,
            CloudinaryApiSecret = cloudinaryApiSecret,
            GhnApiBase = ghnApiBase,
            GhnToken = (configuration["GHN_TOKEN"] ?? string.Empty).Trim(),
            GhnShopId = (configuration["GHN_SHOP_ID"] ?? string.Empty).Trim(),
            GhnWebhookSecret = (configuration["GHN_WEBHOOK_SECRET"] ?? string.Empty).Trim(),
            GhnLiveTests = ReadZeroOrOneFlag(configuration, "GHN_LIVE_TESTS"),
            ApiDocsEnabled = ReadZeroOrOneFlag(configuration, "API_DOCS_ENABLED"),
            ApiDocsInteractive = ReadZeroOrOneFlag(configuration, "API_DOCS_INTERACTIVE"),
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

    // Biến tùy chọn. Rỗng nghĩa là không giới hạn thêm. Giá trị ngoài khoảng làm API dừng khi khởi động.
    private static int? ReadOptionalIntegerInRange(IConfiguration configuration, string key, int minimum, int maximum)
    {
        var rawValue = configuration[key];
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var isValid = int.TryParse(rawValue.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value >= minimum
            && value <= maximum;
        if (!isValid)
        {
            // Thông báo chỉ nêu tên biến và khoảng hợp lệ, không in lại giá trị đã nhập.
            throw new InvalidOperationException($"Biến môi trường {key} phải là số nguyên từ {minimum} đến {maximum}.");
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
        return ReadZeroOrOneFlag(configuration, "AI_LIVE_TESTS");
    }

    private static bool ReadZeroOrOneFlag(IConfiguration configuration, string key)
    {
        var rawValue = configuration[key];
        if (string.IsNullOrWhiteSpace(rawValue) || rawValue == "0")
        {
            return false;
        }

        // Chỉ nhận `1`, giống bản Node. Giá trị như `true` bị từ chối, để lỗi gõ không bật nhầm test tốn tiền hoặc tài liệu API.
        if (rawValue == "1")
        {
            return true;
        }

        throw new InvalidOperationException($"Biến môi trường {key} chỉ nhận giá trị 0 hoặc 1.");
    }

    private static bool IsValidAbsoluteUrl(string url)
    {
        // Xác minh chuỗi cung cấp thỏa mãn định dạng URL tuyệt đối với giao thức http hoặc https.
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
