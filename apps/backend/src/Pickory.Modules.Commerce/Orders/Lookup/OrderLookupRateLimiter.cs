using Pickory.Shared.Exceptions;
using Pickory.Shared.Throttling;

namespace Pickory.Modules.Commerce.Orders.Lookup;

// Giới hạn tần suất tra cứu đơn hàng theo địa chỉ IP và theo email.
// Bộ đếm nằm trong bộ nhớ, nên giới hạn chỉ đúng khi API chạy một instance.
public sealed class OrderLookupRateLimiter(TimeProvider timeProvider)
{
    public const int MaxRequestsPerWindow = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private const string DefaultErrorMessage = "Bạn đã yêu cầu tra cứu đơn quá nhiều lần. Vui lòng thử lại sau 15 phút.";

    private readonly RateLimiter _ipLimiter = new(
        Window,
        MaxRequestsPerWindow,
        DefaultErrorMessage,
        timeProvider);

    private readonly RateLimiter _emailLimiter = new(
        Window,
        MaxRequestsPerWindow,
        DefaultErrorMessage,
        timeProvider);

    // Kiểm tra giới hạn của cả IP và email trước khi tính lượt gọi.
    public void CheckAndConsume(string clientIp, string normalizedEmail)
    {
        var ipKey = clientIp.Trim();
        var emailKey = normalizedEmail.Trim().ToLowerInvariant();

        // Kiểm tra cả hai bộ trước để không tính thêm lượt khi một bên đã hết hạn mức.
        var isIpLimited = _ipLimiter.IsRateLimited(ipKey);
        var isEmailLimited = _emailLimiter.IsRateLimited(emailKey);

        if (isIpLimited || isEmailLimited)
        {
            throw new AppException("TOO_MANY_REQUESTS", 429, DefaultErrorMessage);
        }

        // Cả hai bộ đều còn lượt thì ghi nhận lượt gọi cho cả hai.
        _ipLimiter.RecordAttempt(ipKey);
        _emailLimiter.RecordAttempt(emailKey);
    }

    // Đặt lại toàn bộ bộ đếm trong bộ nhớ để các test không ảnh hưởng lẫn nhau.
    public void Clear()
    {
        _ipLimiter.Clear();
        _emailLimiter.Clear();
    }
}
