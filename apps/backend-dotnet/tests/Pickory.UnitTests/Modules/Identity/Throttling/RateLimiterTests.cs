using Pickory.Modules.Identity.Throttling;
using Pickory.Shared.Exceptions;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Throttling;

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

    [Fact]
    // Đăng ký cho phép 5 lần mỗi giờ mỗi IP. Bot không tạo được hàng loạt tài khoản rác.
    public void RegisterLimiter_AllowsFivePerHourPerIp()
    {
        var limiters = new IdentityRateLimiters(_time);

        // Năm lần đầu thành công.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            limiters.Register.Consume("192.168.1.100");
        }

        // Lần thứ 6 nhận 429 với thông điệp giống bản Node.
        var exception = Assert.Throws<AppException>(() => limiters.Register.Consume("192.168.1.100"));
        Assert.Equal(429, exception.StatusCode);
        Assert.Equal("Bạn đã đăng ký quá nhiều lần từ địa chỉ này. Vui lòng thử lại sau 1 giờ.", exception.Message);

        // Sau một giờ, IP đăng ký lại được.
        _time.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)));
        limiters.Register.Consume("192.168.1.100");
    }

    [Fact]
    // Quên mật khẩu cho phép 3 lần mỗi 15 phút mỗi email. Kẻ xấu không spam được hòm thư của nạn nhân.
    public void ForgotPasswordEmailLimiter_AllowsThreePerFifteenMinutesPerEmail()
    {
        var limiters = new IdentityRateLimiters(_time);

        // Dùng `IsRateLimited` vì quên mật khẩu không được trả 429.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.False(limiters.ForgotPasswordEmail.IsRateLimited("customer@pickory.local"));
            limiters.ForgotPasswordEmail.RecordAttempt("customer@pickory.local");
        }

        // Email bị chặn sau lần thứ 3 và được mở lại sau 15 phút.
        Assert.True(limiters.ForgotPasswordEmail.IsRateLimited("customer@pickory.local"));
        _time.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));
        Assert.False(limiters.ForgotPasswordEmail.IsRateLimited("customer@pickory.local"));
    }

    [Fact]
    // Quên mật khẩu cho phép 10 lần mỗi giờ mỗi IP. Một máy không quét được hàng loạt email.
    public void ForgotPasswordIpLimiter_AllowsTenPerHourPerIp()
    {
        var limiters = new IdentityRateLimiters(_time);

        // Mười lần đầu chưa bị chặn.
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            Assert.False(limiters.ForgotPasswordIp.IsRateLimited("10.10.10.10"));
            limiters.ForgotPasswordIp.RecordAttempt("10.10.10.10");
        }

        // IP bị chặn sau lần thứ 10 và được mở lại sau một giờ.
        Assert.True(limiters.ForgotPasswordIp.IsRateLimited("10.10.10.10"));
        _time.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)));
        Assert.False(limiters.ForgotPasswordIp.IsRateLimited("10.10.10.10"));
    }

    [Fact]
    // `ClearAll` xóa mọi bộ đếm. Test tích hợp dùng hàm này để các test không chặn lẫn nhau.
    public void ClearAll_ResetsEveryLimiterAndLoginThrottle()
    {
        var limiters = new IdentityRateLimiters(_time);
        // Làm đầy bộ giới hạn đăng ký và khóa đăng nhập.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            limiters.Register.Consume("1.1.1.1");
            limiters.Login.RecordFailure("a@example.com", "1.1.1.1");
        }

        limiters.ClearAll();

        // Cả hai bộ đếm cho qua ngay, không cần chờ hết cửa sổ.
        limiters.Register.Consume("1.1.1.1");
        limiters.Login.AssertNotLocked("a@example.com", "1.1.1.1");
    }
}
