using Microsoft.AspNetCore.Http;

namespace Pickory.Shared.Http;

// Tiền tố route nhận request từ hệ thống bên ngoài. Bên ngoài gọi từ server nên không có `Origin`, cookie hay phiên đăng nhập.
public static class WebhookRoutes
{
    // So khớp theo từng đoạn đường dẫn. `/api/webhooks-khac` không thuộc tiền tố này.
    public static readonly PathString PathPrefix = new("/api/webhooks");
}
