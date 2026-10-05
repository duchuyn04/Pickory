using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Throttling;

// Đếm lần đăng nhập sai theo cặp email và IP. Bộ đếm nằm trong bộ nhớ, đủ dùng khi API chạy một instance.
public sealed class LoginThrottle(TimeProvider timeProvider)
{
    // Cửa sổ đếm và thời gian khóa cùng là 15 phút theo spec.
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    // Sai 5 lần thì lần thử thứ 6 bị chặn.
    private const int MaxFailures = 5;

    private readonly Dictionary<string, FailureWindow> _windows = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    // Gọi trước khi kiểm tra mật khẩu. Người đang bị khóa không làm máy chủ tốn CPU băm Argon2.
    public void AssertNotLocked(string email, string ip)
    {
        lock (_gate)
        {
            var key = CreateKey(email, ip);
            var now = timeProvider.GetUtcNow();
            if (!_windows.TryGetValue(key, out var failureWindow))
            {
                return;
            }

            // Cửa sổ đã hết thì xóa bản ghi. Người dùng được thử lại và bộ nhớ không phình mãi.
            if (now >= failureWindow.ResetAt)
            {
                _windows.Remove(key);
                return;
            }

            if (failureWindow.LockedUntil > now)
            {
                throw new AppException(
                    "TOO_MANY_ATTEMPTS",
                    429,
                    "Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau 15 phút.");
            }
        }
    }

    public void RecordFailure(string email, string ip)
    {
        lock (_gate)
        {
            var key = CreateKey(email, ip);
            var now = timeProvider.GetUtcNow();
            // Lần sai đầu tiên, hoặc cửa sổ cũ đã hết, bắt đầu đếm lại từ 1.
            if (!_windows.TryGetValue(key, out var failureWindow) || now >= failureWindow.ResetAt)
            {
                _windows[key] = new FailureWindow(1, DateTimeOffset.MinValue, now.Add(Window));
                return;
            }

            var failures = failureWindow.Failures + 1;
            // Lần sai thứ 5 vẫn trả 401, lần thử thứ 6 mới nhận 429.
            // Thời điểm xóa bản ghi dời theo khóa, nên bản ghi không biến mất trước khi hết khóa.
            _windows[key] = failures >= MaxFailures
                ? new FailureWindow(failures, now.Add(Window), now.Add(Window))
                : failureWindow with { Failures = failures };
        }
    }

    // Đăng nhập đúng thì xóa bộ đếm. Lần gõ nhầm trước đó không cộng dồn vào lần sau.
    public void RecordSuccess(string email, string ip)
    {
        lock (_gate)
        {
            _windows.Remove(CreateKey(email, ip));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _windows.Clear();
        }
    }

    // Đếm theo cặp email và IP. Kẻ tấn công ở IP lạ không khóa được tài khoản của người khác.
    private static string CreateKey(string email, string ip)
    {
        return $"{email.Trim().ToLowerInvariant()}|{ip.Trim()}";
    }

    private sealed record FailureWindow(int Failures, DateTimeOffset LockedUntil, DateTimeOffset ResetAt);
}
