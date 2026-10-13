using Pickory.Shared.Calendar;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.AiGateway.RateLimiting;

// Hạn mức chat theo người trong một ngày giờ Việt Nam.
// Bộ đếm nằm trong bộ nhớ, nên khi API chạy nhiều instance phải chuyển bộ đếm sang DB.
public sealed class ChatRateLimiter(TimeProvider timeProvider)
{
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);
    // Request chạy song song trên nhiều luồng. Khóa giữ bộ đếm không bị ghi đè lẫn nhau.
    private readonly Lock _gate = new();
    private string _currentDay = string.Empty;

    // Tăng lượt của actor và trả ngày của lượt đó. Gateway cần ngày này để hoàn lượt đúng ngày.
    public string CheckAndIncrement(string? actorKey, int userLimit, int anonLimit)
    {
        // Chat phải có định danh. Thiếu định danh thì hạn mức ngày bị bỏ qua.
        if (string.IsNullOrEmpty(actorKey))
        {
            throw new AppException(
                "INVALID_ACTOR_KEY",
                400,
                "actorKey là bắt buộc cho tính năng trò chuyện và phải có tiền tố user: hoặc anon:.");
        }

        var trimmedKey = actorKey.Trim();
        var isUser = trimmedKey.StartsWith("user:", StringComparison.Ordinal);
        var isAnonymous = trimmedKey.StartsWith("anon:", StringComparison.Ordinal);
        // Tiền tố quyết định hạn mức áp dụng. Khóa sai tiền tố là lỗi lập trình của module gọi.
        if (!isUser && !isAnonymous)
        {
            throw new AppException(
                "INVALID_ACTOR_KEY",
                400,
                "actorKey sai định dạng: phải bắt đầu bằng \"user:\" cho tài khoản hoặc \"anon:\" cho khách vãng lai.");
        }

        // Định danh rỗng sau tiền tố làm mọi khách dùng chung một bộ đếm.
        if (trimmedKey[(trimmedKey.IndexOf(':') + 1)..].Trim().Length == 0)
        {
            throw new AppException(
                "INVALID_ACTOR_KEY",
                400,
                "actorKey không hợp lệ: định danh sau tiền tố không được để trống.");
        }

        lock (_gate)
        {
            var today = VietnamCalendar.DayKey(timeProvider.GetUtcNow());
            // Sang ngày mới thì xóa toàn bộ bộ đếm một lần. Bộ nhớ không giữ lượt của các ngày cũ.
            if (today != _currentDay)
            {
                _counters.Clear();
                _currentDay = today;
            }

            var currentCount = _counters.GetValueOrDefault(trimmedKey);
            var limit = isUser ? userLimit : anonLimit;
            // Chặn khi đã dùng hết lượt. Khách vãng lai được gợi ý đăng nhập để có thêm lượt.
            if (currentCount >= limit)
            {
                var message = isAnonymous
                    ? $"Bạn đã dùng hết {limit} lượt trò chuyện miễn phí trong ngày. Vui lòng đăng nhập hoặc quay lại vào ngày mai."
                    : $"Bạn đã đạt giới hạn {limit} lượt trò chuyện trong ngày. Vui lòng quay lại vào ngày mai.";
                throw new AiBudgetExceededException(message);
            }

            // Một lời gọi chỉ chiếm một lượt, dù vòng lặp tool hoặc lần sửa JSON gọi provider nhiều lần.
            _counters[trimmedKey] = currentCount + 1;
            return today;
        }
    }

    public void Refund(string actorKey, string day)
    {
        lock (_gate)
        {
            // Lượt lỗi của hôm trước không được trừ vào bộ đếm của ngày mới.
            if (day != _currentDay)
            {
                return;
            }

            var key = actorKey.Trim();
            var count = _counters.GetValueOrDefault(key);
            if (count <= 1)
            {
                _counters.Remove(key);
                return;
            }

            _counters[key] = count - 1;
        }
    }
}
