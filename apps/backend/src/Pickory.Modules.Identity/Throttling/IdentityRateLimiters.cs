namespace Pickory.Modules.Identity.Throttling;

// Mọi bộ giới hạn của identity trong một singleton. Ngưỡng và thông báo giữ nguyên bản Node.
public sealed class IdentityRateLimiters(TimeProvider timeProvider)
{
    // Đăng ký: 5 lần mỗi giờ mỗi IP. Bot không tạo được hàng loạt tài khoản rác.
    public RateLimiter Register { get; } = new(
        TimeSpan.FromHours(1),
        5,
        "Bạn đã đăng ký quá nhiều lần từ địa chỉ này. Vui lòng thử lại sau 1 giờ.",
        timeProvider);

    // Quên mật khẩu theo email: 3 lần mỗi 15 phút. Kẻ xấu không spam được hòm thư của nạn nhân.
    public RateLimiter ForgotPasswordEmail { get; } = new(
        TimeSpan.FromMinutes(15),
        3,
        "Yêu cầu đặt lại mật khẩu quá thường xuyên cho email này.",
        timeProvider);

    // Quên mật khẩu theo IP: 10 lần mỗi giờ. Một máy không quét được hàng loạt email.
    public RateLimiter ForgotPasswordIp { get; } = new(
        TimeSpan.FromHours(1),
        10,
        "Yêu cầu đặt lại mật khẩu quá thường xuyên từ IP này.",
        timeProvider);

    public LoginThrottle Login { get; } = new(timeProvider);

    // Test gọi hàm này để các test không ảnh hưởng nhau qua bộ đếm dùng chung.
    public void ClearAll()
    {
        Register.Clear();
        ForgotPasswordEmail.Clear();
        ForgotPasswordIp.Clear();
        Login.Clear();
    }
}
