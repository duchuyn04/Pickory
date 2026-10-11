using Pickory.Shared.Exceptions;
using Pickory.Shared.Throttling;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Shared.Throttling;

// Port từ test ID-17 của bản Node.
public class RateLimiterTests
{
    // Đồng hồ cố định do test điều khiển. Test kiểm tra cửa sổ thời gian mà không phải chờ thật.
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    // Bộ giới hạn chặn khi đạt ngưỡng, mở lại sau cửa sổ và đếm riêng từng khóa.
    public void Limiter_BlocksAtThresholdReopensAfterWindowAndKeepsKeysIndependent()
    {
        // Ngưỡng 3 lần mỗi 60 giây giữ test ngắn.
        var limiter = new RateLimiter(TimeSpan.FromSeconds(60), 3, "Vượt ngưỡng thử nghiệm.", _time);

        // Ba lần đầu chưa bị chặn.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            limiter.AssertNotRateLimited("ip-a");
            Assert.False(limiter.IsRateLimited("ip-a"));
            limiter.RecordAttempt("ip-a");
        }

        // Sau lần thứ 3, cả ba cách hỏi đều thấy khóa bị chặn với 429.
        Assert.True(limiter.IsRateLimited("ip-a"));
        var assertException = Assert.Throws<AppException>(() => limiter.AssertNotRateLimited("ip-a"));
        Assert.Equal(429, assertException.StatusCode);
        Assert.Equal("TOO_MANY_REQUESTS", assertException.Code);
        Assert.Equal("Vượt ngưỡng thử nghiệm.", assertException.Message);
        Assert.Throws<AppException>(() => limiter.Consume("ip-a"));

        // Khóa khác có bộ đếm riêng. Một địa chỉ IP (Internet Protocol) bị chặn không chặn IP khác.
        Assert.False(limiter.IsRateLimited("ip-b"));
        limiter.Consume("ip-b");

        // Đẩy đồng hồ qua hết cửa sổ 60 giây.
        _time.Advance(TimeSpan.FromMilliseconds(60_100));

        // Khóa đã bị chặn được mở lại sau khi cửa sổ hết.
        Assert.False(limiter.IsRateLimited("ip-a"));
        limiter.Consume("ip-a");
    }
}
