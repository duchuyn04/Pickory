using Pickory.Shared.Configuration;
using Pickory.Shared.Http;

namespace Pickory.Api.ChatbotWidget;

// Route công khai cho frontend biết có hiện khung chat hay không. Route không đọc DB.
public static class ChatbotWidgetEndpoint
{
    public static void MapChatbotWidgetEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/chatbot-widget", (AppConfig config) =>
        {
            // Thiếu cấu hình thì trả 200 với `enabled: false`. Frontend không chèn script và trang chạy như cũ.
            if (!config.HasChatbotWidget)
            {
                return Results.Ok(new ApiResponse<ChatbotWidgetResponse>(new ChatbotWidgetResponse(false, null, null)));
            }

            // Địa chỉ đến từ biến môi trường, nên một bản build frontend chạy được trên nhiều môi trường.
            return Results.Ok(new ApiResponse<ChatbotWidgetResponse>(
                new ChatbotWidgetResponse(true, config.ChatbotWidgetScriptUrl, config.ChatbotApiUrl)));
        }).WithTags("System");
    }
}

// Dữ liệu trả cho frontend. Hai địa chỉ là null khi `Enabled` là false.
public sealed record ChatbotWidgetResponse(bool Enabled, string? ScriptUrl, string? ApiUrl);
