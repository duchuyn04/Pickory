using System.ClientModel;
using System.Net.Sockets;
using Pickory.Modules.AiGateway.Resilience;

namespace Pickory.Modules.AiGateway.Providers;

// Đổi mọi lỗi của SDK hoặc HTTP thành `ProviderException`.
// Lỗi chuẩn hóa không giữ thông báo gốc, URL hay khóa, nên không lộ bí mật vào log.
public static class ProviderErrorMapper
{
    public static ProviderException Map(Exception exception)
    {
        if (exception is ProviderException providerException)
        {
            return providerException;
        }

        // Provider đã trả mã HTTP. Chỉ 429 và 5xx là sự cố tạm thời, các mã 4xx khác lặp lại cũng lỗi.
        if (exception is ClientResultException { Status: > 0 } clientResult)
        {
            var status = clientResult.Status;
            var retryable = status == 429 || status is >= 500 and < 600;
            return new ProviderException(status, retryable);
        }

        // Lỗi mạng, mất kết nối hoặc hết thời gian chờ chưa có mã HTTP. Thử lại có thể thành công.
        if (IsNetworkError(exception))
        {
            return new ProviderException(null, retryable: true);
        }

        // Lỗi parse hoặc lỗi lập trình. Thử lại cũng cho cùng kết quả.
        return new ProviderException(null, retryable: false);
    }

    private static bool IsNetworkError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or IOException or SocketException or TimeoutException or OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }
}
