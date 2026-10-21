using Pickory.Shared.Exceptions;

namespace Pickory.Shared.Throttling;

// Giới hạn số lần gọi theo một khóa (IP hoặc email) trong một cửa sổ thời gian.
// Bộ đếm nằm trong bộ nhớ, nên giới hạn chỉ đúng khi API chạy một instance.
public sealed class RateLimiter(TimeSpan window, int maxAttempts, string errorMessage, TimeProvider timeProvider)
{
    private readonly Dictionary<string, AttemptWindow> _windows = new(StringComparer.Ordinal);
    // Request chạy song song trên nhiều luồng. Khóa giữ bộ đếm không bị ghi đè lẫn nhau.
    private readonly Lock _gate = new();
    // Mốc đến hạn của lần dọn kế tiếp. Lần dọn đầu tiên đến hạn sau một cửa sổ kể từ lúc dựng limiter.
    private DateTimeOffset _nextCleanupAt = timeProvider.GetUtcNow().Add(window);

    // Test đếm số khóa còn trong bộ nhớ để kiểm bước dọn. Code chạy thật không dùng thuộc tính này.
    internal int TrackedKeyCount
    {
        get
        {
            lock (_gate)
            {
                return _windows.Count;
            }
        }
    }

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

            // Cửa sổ của khóa đang hỏi đã hết thì xóa bản ghi. `RemoveExpiredWindowsWhenDue` xóa các khóa không còn ai gọi lại.
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
            // Dọn trước khi ghi lượt. Khóa chỉ gọi một lần rồi bỏ đi (IP lạ, email lạ) không nằm lại trong bộ nhớ mãi.
            RemoveExpiredWindowsWhenDue(now);

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

    // Gọi trong `lock`. Mỗi cửa sổ chỉ duyệt cả bảng một lần, nên request thường không tốn thời gian tỷ lệ với số khóa.
    private void RemoveExpiredWindowsWhenDue(DateTimeOffset now)
    {
        if (now < _nextCleanupAt)
        {
            return;
        }

        // Xóa trong lúc duyệt là hợp lệ với `Dictionary` từ .NET Core 3.0.
        foreach (var (key, attemptWindow) in _windows)
        {
            if (now >= attemptWindow.ResetAt)
            {
                _windows.Remove(key);
            }
        }

        // Đặt mốc sau lần dọn này một cửa sổ. Bản ghi hết hạn giữa hai lần dọn chờ đến lần dọn kế tiếp.
        _nextCleanupAt = now.Add(window);
    }

    private sealed record AttemptWindow(int Attempts, DateTimeOffset ResetAt);
}
