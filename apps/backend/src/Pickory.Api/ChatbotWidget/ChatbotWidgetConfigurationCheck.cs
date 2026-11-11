using Pickory.Shared.Configuration;

namespace Pickory.Api.ChatbotWidget;

// Kiểm địa chỉ dịch vụ chatbot lúc khởi động. Ngoài Development, địa chỉ phải dùng `https`.
public static class ChatbotWidgetConfigurationCheck
{
    // Trang khách chạy trên `https` sẽ chặn script `http` (mixed content). Dừng sớm thay vì để khung chat im lặng biến mất.
    public static void EnsureSecureOutsideDevelopment(AppConfig config, bool isDevelopment)
    {
        // Máy dev chạy `chatbot-rag` ở `http://localhost`, nên Development được dùng `http`.
        if (isDevelopment)
        {
            return;
        }

        // Không cấu hình chatbot thì không có địa chỉ nào để kiểm.
        if (!config.HasChatbotWidget)
        {
            return;
        }

        // Thông báo chỉ nêu tên biến, không in lại địa chỉ đã nhập.
        if (!IsHttps(config.ChatbotWidgetScriptUrl))
        {
            throw new InvalidOperationException("Biến môi trường CHATBOT_WIDGET_SCRIPT_URL phải dùng https ngoài môi trường Development.");
        }

        if (!IsHttps(config.ChatbotApiUrl))
        {
            throw new InvalidOperationException("Biến môi trường CHATBOT_API_URL phải dùng https ngoài môi trường Development.");
        }
    }

    private static bool IsHttps(string url)
    {
        // `AppConfig.Load` đã bảo đảm đây là URL tuyệt đối, nên chỉ cần so sánh giao thức.
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }
}
