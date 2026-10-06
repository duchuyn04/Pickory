using Pickory.Modules.Identity.Throttling;
using Pickory.Shared.Exceptions;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Throttling;

// Port từ test ID-06 của bản Node.
public class LoginThrottleTests
{
    // Đồng hồ cố định do test điều khiển. Test kiểm tra hạn khóa mà không phải chờ 15 phút thật.
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    // Lần sai thứ 5 vẫn được thử. Lần thử thứ 6 bị khóa, giống bản Node.
    public void FifthFailureStillAllowedSixthAttemptLocked()
    {
        var throttle = new LoginThrottle(_time);

        // Mỗi lần thử gọi `AssertNotLocked` trước `RecordFailure`, giống thứ tự của handler đăng nhập.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            throttle.AssertNotLocked("victim@example.com", "192.168.1.1");
            throttle.RecordFailure("victim@example.com", "192.168.1.1");
        }

        // Lần thử thứ 6 nhận 429 với mã và thông điệp giống bản Node.
        var exception = Assert.Throws<AppException>(() => throttle.AssertNotLocked("victim@example.com", "192.168.1.1"));
        Assert.Equal(429, exception.StatusCode);
        Assert.Equal("TOO_MANY_ATTEMPTS", exception.Code);
        Assert.Equal("Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau 15 phút.", exception.Message);
    }

    [Fact]
    // Khóa hết hạn sau 15 phút. Người dùng thật được thử lại mà không cần hỗ trợ.
    public void LockExpiresAfterFifteenMinutes()
    {
        var throttle = new LoginThrottle(_time);
        // Ghi đủ 5 lần sai để kích hoạt khóa.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            throttle.RecordFailure("user@example.com", "10.0.0.1");
        }

        Assert.Throws<AppException>(() => throttle.AssertNotLocked("user@example.com", "10.0.0.1"));
        // Đẩy đồng hồ qua mốc 15 phút thêm một giây.
        _time.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));

        // Hàm không ném lỗi nghĩa là khóa đã hết.
        throttle.AssertNotLocked("user@example.com", "10.0.0.1");
    }

    [Fact]
    // Bộ đếm theo cặp email và địa chỉ IP (Internet Protocol). Kẻ tấn công ở IP lạ không khóa được tài khoản của người khác.
    public void OtherEmailOrIpPairsAreNotAffected()
    {
        var throttle = new LoginThrottle(_time);
        // Khóa riêng cặp `user1@example.com` và `1.1.1.1`.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            throttle.RecordFailure("user1@example.com", "1.1.1.1");
        }

        Assert.Throws<AppException>(() => throttle.AssertNotLocked("user1@example.com", "1.1.1.1"));
        // Cùng email từ IP khác và email khác từ cùng IP đều không bị khóa.
        throttle.AssertNotLocked("user1@example.com", "2.2.2.2");
        throttle.AssertNotLocked("user2@example.com", "1.1.1.1");
    }

    [Fact]
    // Email khác chữ hoa hoặc có khoảng trắng dùng chung bộ đếm. Kẻ dò không né khóa bằng cách đổi cách viết email.
    public void EmailCaseAndSurroundingSpacesShareOneCounter()
    {
        var throttle = new LoginThrottle(_time);
        // Xen kẽ hai cách viết của cùng một email trong 5 lần sai.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            throttle.RecordFailure(attempt % 2 == 0 ? " Victim@Example.com " : "victim@example.com", "1.1.1.1");
        }

        // Cách viết thứ ba cũng thấy khóa.
        Assert.Throws<AppException>(() => throttle.AssertNotLocked("VICTIM@example.com", "1.1.1.1"));
    }

    [Fact]
    // Đăng nhập đúng xóa bộ đếm. Lần gõ nhầm trước đó không cộng dồn vào lần sau.
    public void SuccessClearsFailureCounter()
    {
        var throttle = new LoginThrottle(_time);
        // Ghi 4 lần sai, ngay dưới ngưỡng khóa.
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            throttle.RecordFailure("user@example.com", "1.1.1.1");
        }

        // Lần đúng xóa bộ đếm trước lần sai tiếp theo. Không xóa bộ đếm thì lần sai này là lần thứ 5.
        throttle.RecordSuccess("user@example.com", "1.1.1.1");
        throttle.RecordFailure("user@example.com", "1.1.1.1");

        // Hàm không ném lỗi nghĩa là bộ đếm đã bắt đầu lại từ 1.
        throttle.AssertNotLocked("user@example.com", "1.1.1.1");
    }
}
