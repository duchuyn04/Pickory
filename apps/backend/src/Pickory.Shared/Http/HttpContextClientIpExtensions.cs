using Microsoft.AspNetCore.Http;

namespace Pickory.Shared.Http;

// Một chỗ đọc IP của client cho mọi bộ giới hạn tần suất.
public static class HttpContextClientIpExtensions
{
    public static string GetClientIp(this HttpContext context)
    {
        // `127.0.0.1` chỉ là giá trị dự phòng khi máy chủ test không có địa chỉ kết nối, giống bản Node.
        return context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }
}
