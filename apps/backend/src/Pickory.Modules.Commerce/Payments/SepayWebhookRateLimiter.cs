using Pickory.Shared.Throttling;

namespace Pickory.Modules.Commerce.Payments;

// Bộ đếm theo IP nằm trong bộ nhớ, nên giới hạn chỉ áp dụng trong một instance API.
internal sealed class SepayWebhookRateLimiter(TimeProvider timeProvider)
{
    public const int MaxRequestsPerWindow = 60;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly RateLimiter _limiter = new(
        Window, MaxRequestsPerWindow,
        "Webhook nhận quá nhiều request từ địa chỉ này. Vui lòng thử lại sau.", timeProvider);

    // Đếm cả request sai khóa để chặn dò khóa trước mọi lần đọc body hoặc truy cập DB.
    public void Consume(string clientIp) => _limiter.Consume(clientIp);

}
