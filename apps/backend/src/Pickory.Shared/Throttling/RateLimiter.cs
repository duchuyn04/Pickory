using Pickory.Shared.Exceptions;

namespace Pickory.Shared.Throttling;

// Giới hạn số lần gọi theo một khóa (IP hoặc email) trong một cửa sổ thời gian.
// Bộ đếm nằm trong bộ nhớ, nên giới hạn chỉ đúng khi API chạy một instance.
public sealed class RateLimiter(TimeSpan window, int maxAttempts, string errorMessage, TimeProvider timeProvider)
{
    private readonly Dictionary<string, AttemptWindow> _windows = new(StringComparer.Ordinal);
    // Request chạy song song trên nhiều luồng. Khóa giữ bộ đếm không bị ghi đè lẫn nhau.
    private readonly Lock _gate = new();

    // Ném 429 khi khóa đã dùng hết số lần trong cửa sổ hiện tại.
    public void AssertNotRateLimited(string key)
    {
        if (IsRateLimited(key))
        {
            throw new AppException("TOO_MANY_REQUESTS", 429, errorMessage);
        }
    }

    // Trả kết quả thay vì ném lỗi. Quên mật khẩu không được báo 429, vì lỗi đó cho biết email đang bị gọi nhiều.
    public bool IsRateLimited(string key)
    {
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            if (!_windows.TryGetValue(key, out var attemptWindow))
            {
                return false;
            }

            // Cửa sổ đã hết thì xóa bản ghi. Khóa được gọi lại và bộ nhớ không phình mãi.
            if (now >= attemptWindow.ResetAt)
            {
                _windows.Remove(key);
                return false;
            }

            return attemptWindow.Attempts >= maxAttempts;
        }
    }

    public void RecordAttempt(string key)
    {
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            // Khóa mới hoặc cửa sổ cũ đã hết thì bắt đầu cửa sổ mới.
            if (!_windows.TryGetValue(key, out var attemptWindow) || now >= attemptWindow.ResetAt)
            {
                _windows[key] = new AttemptWindow(1, now.Add(window));
                return;
            }

            // Cửa sổ còn hiệu lực thì cộng thêm một lần. Thời điểm hết cửa sổ giữ nguyên.
            _windows[key] = attemptWindow with { Attempts = attemptWindow.Attempts + 1 };
        }
    }

    // Kiểm tra trước khi tính lượt. Request bị chặn không kéo dài thời gian bị chặn.
    public void Consume(string key)
    {
        AssertNotRateLimited(key);
        RecordAttempt(key);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _windows.Clear();
        }
    }

    private sealed record AttemptWindow(int Attempts, DateTimeOffset ResetAt);
}
