using Pickory.Shared.Configuration;

namespace Pickory.Modules.Shipping.Configuration;

// Kiểm biến cấu hình GHN lúc khởi động. Production thiếu biến thì API dừng, dev và test dùng client giả.
public static class GhnConfigurationCheck
{
    // Production thiếu biến thì dừng ngay, thay vì chạy mà không gọi được GHN hoặc không xác thực được webhook.
    public static void EnsureConfigured(AppConfig config, bool isProduction)
    {
        // Dev và test không cần biến GHN. Module dùng client giả khi thiếu token.
        if (!isProduction)
        {
            return;
        }

        // Chỉ ghi tên biến thiếu. Thông báo không chứa giá trị của biến nào, để token và secret không lọt vào log.
        var missingNames = new List<string>();
        if (string.IsNullOrWhiteSpace(config.GhnToken))
        {
            missingNames.Add("GHN_TOKEN");
        }

        if (string.IsNullOrWhiteSpace(config.GhnShopId))
        {
            missingNames.Add("GHN_SHOP_ID");
        }

        if (string.IsNullOrWhiteSpace(config.GhnWebhookSecret))
        {
            missingNames.Add("GHN_WEBHOOK_SECRET");
        }

        if (missingNames.Count > 0)
        {
            throw new InvalidOperationException($"Production thiếu biến môi trường: {string.Join(", ", missingNames)}.");
        }
    }
}
