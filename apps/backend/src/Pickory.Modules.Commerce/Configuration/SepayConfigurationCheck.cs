using Pickory.Shared.Configuration;

namespace Pickory.Modules.Commerce.Configuration;

// Kiểm biến cấu hình SePay lúc khởi động. Production thiếu khóa thì API dừng, dev và test vẫn chạy.
public static class SepayConfigurationCheck
{
    public static void EnsureConfigured(AppConfig config, bool isProduction)
    {
        // Dev và test không nhận webhook thật, nên thiếu khóa không làm hỏng luồng nào.
        if (!isProduction)
        {
            return;
        }

        // Thiếu khóa thì webhook không xác thực được và tiền khách chuyển không được ghi nhận. Dừng ngay khi khởi động.
        if (string.IsNullOrWhiteSpace(config.SepayWebhookApiKey))
        {
            // Thông báo chỉ ghi tên biến, để giá trị khóa không lọt vào log.
            throw new InvalidOperationException("Production thiếu biến môi trường: SEPAY_WEBHOOK_API_KEY.");
        }
    }
}
