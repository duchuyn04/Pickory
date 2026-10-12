using Pickory.Shared.Throttling;

namespace Pickory.Modules.Commerce.Checkout;

// Giới hạn tần suất của route preview theo từng khách. Bộ đếm nằm trong bộ nhớ, nên giới hạn chỉ đúng khi API chạy một instance.
public sealed class CheckoutPreviewRateLimiter(TimeProvider timeProvider)
{
    // Mỗi lần preview gọi GHN một lần. 20 lần mỗi phút đủ cho khách đổi địa chỉ và phương thức, và chặn được việc gọi GHN hàng loạt.
    public const int MaxRequestsPerWindow = 20;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly RateLimiter _limiter = new(
        Window,
        MaxRequestsPerWindow,
        "Bạn đã tính phí vận chuyển quá nhiều lần. Vui lòng thử lại sau.",
        timeProvider);

    // Khách đã đăng nhập đi theo `UserId`. Khách vãng lai đi theo IP, vì khách bỏ cookie sẽ nhận `AnonymousId` mới.
    public static string BuildKey(string? userId, string clientIp) =>
        userId is null ? $"ip:{clientIp}" : $"user:{userId}";

    // Ném 429 khi khóa hết lượt, và chỉ tính lượt khi còn trong giới hạn.
    public void Consume(string key) => _limiter.Consume(key);

    // Test gọi hàm này để các test không ảnh hưởng nhau qua bộ đếm dùng chung.
    public void Clear() => _limiter.Clear();
}
