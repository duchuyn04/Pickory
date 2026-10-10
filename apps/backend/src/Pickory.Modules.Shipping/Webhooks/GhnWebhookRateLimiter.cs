using Pickory.Shared.Throttling;

namespace Pickory.Modules.Shipping.Webhooks;

// Giới hạn tần suất của webhook GHN theo IP. Bộ đếm nằm trong bộ nhớ, nên giới hạn chỉ đúng khi API chạy một instance.
internal sealed class GhnWebhookRateLimiter(TimeProvider timeProvider)
{
    // GHN gửi cập nhật của mọi đơn từ một nhóm IP nhỏ. 300 request mỗi phút đủ cho cửa hàng nhỏ và vẫn chặn được việc dò secret.
    public const int MaxRequestsPerWindow = 300;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly RateLimiter _limiter = new(
        Window,
        MaxRequestsPerWindow,
        "Webhook nhận quá nhiều request từ địa chỉ này. Vui lòng thử lại sau.",
        timeProvider);

    // Ném 429 khi IP hết lượt, và chỉ tính lượt khi còn trong giới hạn.
    public void Consume(string clientIp) => _limiter.Consume(clientIp);

    // Test gọi hàm này để các test không ảnh hưởng nhau qua bộ đếm dùng chung.
    public void Clear() => _limiter.Clear();
}
