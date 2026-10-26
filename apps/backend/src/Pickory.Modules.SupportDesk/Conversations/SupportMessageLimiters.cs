using Pickory.Shared.Throttling;

namespace Pickory.Modules.SupportDesk.Conversations;

// Hai giới hạn tin của khách: 10 tin mỗi phút và 200 tin mỗi 24 giờ, khóa theo `Actor.AnonymousId`.
// Bộ đếm nằm trong bộ nhớ, nên giới hạn chỉ đúng khi API chạy một instance.
internal sealed class SupportMessageLimiters
{
    public const int MaxMessagesPerMinute = 10;
    public const int MaxMessagesPerDay = 200;
    // Một chuỗi dùng cho cả route (429 cho khách) và `RateLimiter` (bắt buộc có chuỗi lỗi).
    public const string RateLimitedMessage = "Bạn gửi tin quá nhanh. Vui lòng thử lại sau ít phút.";

    private readonly RateLimiter _perMinute;
    private readonly RateLimiter _perDay;
    // Kiểm và ghi lượt phải liền nhau. Hai request cùng lúc không cùng thấy còn một lượt rồi cùng ghi.
    private readonly Lock _gate = new();

    public SupportMessageLimiters(TimeProvider timeProvider)
    {
        _perMinute = new RateLimiter(TimeSpan.FromMinutes(1), MaxMessagesPerMinute, RateLimitedMessage, timeProvider);
        // Cửa sổ 24 giờ tính từ lượt đầu của khóa, không theo ngày lịch. Khách không được thêm 200 tin ngay sau nửa đêm.
        _perDay = new RateLimiter(TimeSpan.FromHours(24), MaxMessagesPerDay, RateLimitedMessage, timeProvider);
    }

    // Trả `false` khi một trong hai giới hạn đã hết lượt. Khi đó không limiter nào ghi thêm lượt.
    public bool TryAcquire(string key)
    {
        lock (_gate)
        {
            // Không dùng `Consume`, vì `Consume` ném mã `TOO_MANY_REQUESTS`. Route cần mã riêng của module.
            if (_perMinute.IsRateLimited(key) || _perDay.IsRateLimited(key))
            {
                return false;
            }

            // Ghi lượt vào cả hai chỉ sau khi cả hai còn chỗ. Tin bị chặn bởi một giới hạn không làm tăng giới hạn kia.
            _perMinute.RecordAttempt(key);
            _perDay.RecordAttempt(key);
            return true;
        }
    }
}
