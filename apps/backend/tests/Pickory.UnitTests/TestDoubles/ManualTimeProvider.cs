namespace Pickory.UnitTests.TestDoubles;

// Đồng hồ do test điều khiển. Test kiểm tra cửa sổ thời gian mà không phải chờ thật.
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    // Trả thời điểm do test đặt. Code đang kiểm tra không đọc đồng hồ hệ thống.
    public override DateTimeOffset GetUtcNow()
    {
        return _now;
    }

    // Đẩy đồng hồ tới trước. Test vượt qua cửa sổ thời gian ngay lập tức.
    public void Advance(TimeSpan duration)
    {
        _now = _now.Add(duration);
    }
}
