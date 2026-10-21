using Pickory.Shared.Throttling;

namespace Pickory.Modules.ActivityTracking.Features.CollectEvents;

// Giới hạn số lượt xem mỗi `UserId` hoặc mỗi IP được gửi, để một client gửi lặp không làm sai bộ đếm.
// Bộ đếm nằm trong bộ nhớ, nên giới hạn chỉ đúng khi API chạy một instance.
internal sealed class ProductViewRateLimiter(TimeProvider timeProvider)
{
    public const int MaxRequestsPerWindow = 60;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private const string ErrorMessage = "Bạn gửi quá nhiều lượt xem. Vui lòng thử lại sau 1 phút.";

    private readonly RateLimiter _limiter = new(Window, MaxRequestsPerWindow, ErrorMessage, timeProvider);

    // Khách đã đăng nhập đi theo `UserId`. Khách vãng lai đi theo IP, vì khách đổi cookie `pk_anon` ở mỗi request sẽ né được khóa theo `AnonymousId`.
    public static string BuildKey(string? userId, string clientIp) =>
        userId is null ? $"ip:{clientIp}" : $"user:{userId}";

    // Kiểm tra trước rồi mới ghi lượt. Request bị chặn không kéo dài thời gian bị chặn.
    public void Consume(string key)
    {
        _limiter.Consume(key);
    }
}
