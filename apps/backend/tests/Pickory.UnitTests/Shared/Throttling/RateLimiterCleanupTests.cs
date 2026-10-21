using Microsoft.Extensions.Time.Testing;
using Pickory.Shared.Throttling;
using Xunit;

namespace Pickory.UnitTests.Shared.Throttling;

// ACT-14: `RateLimiter` tự xóa bản ghi đã hết cửa sổ, và chỉ duyệt cả bảng một lần mỗi cửa sổ. Đồng hồ giả điều khiển từng mốc thời gian.
public sealed class RateLimiterCleanupTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2031, 3, 10, 9, 0, 0, TimeSpan.Zero));

    private RateLimiter CreateLimiter() => new(Window, 3, "Vượt ngưỡng thử nghiệm.", _clock);

    [Fact]
    public void RecordAttempt_AfterWindowPasses_RemovesExpiredKeyThatIsNeverCalledAgain()
    {
        var limiter = CreateLimiter();
        limiter.RecordAttempt("ip:a");
        Assert.Equal(1, limiter.TrackedKeyCount);

        // Khóa `ip:a` hết cửa sổ và không ai gọi lại. Chỉ bước dọn mới xóa được bản ghi này.
        _clock.Advance(Window + TimeSpan.FromSeconds(1));
        limiter.RecordAttempt("ip:b");

        Assert.Equal(1, limiter.TrackedKeyCount);
    }

    [Fact]
    public void RecordAttempt_WhenCleanupRuns_KeepsKeyStillInsideItsWindow()
    {
        var limiter = CreateLimiter();
        limiter.RecordAttempt("ip:expired");
        _clock.Advance(TimeSpan.FromSeconds(30));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            limiter.RecordAttempt("ip:alive");
        }

        // Tại giây 61, `ip:expired` đã hết cửa sổ còn `ip:alive` còn 29 giây.
        _clock.Advance(TimeSpan.FromSeconds(31));
        limiter.RecordAttempt("ip:trigger");

        Assert.Equal(2, limiter.TrackedKeyCount);
        // Bản ghi còn hiệu lực giữ nguyên số lượt, nên khóa vẫn bị chặn.
        Assert.True(limiter.IsRateLimited("ip:alive"));
    }

    [Fact]
    public void RecordAttempt_CleanupRunsAtMostOncePerWindow()
    {
        var limiter = CreateLimiter();
        limiter.RecordAttempt("ip:first");
        _clock.Advance(TimeSpan.FromSeconds(30));
        limiter.RecordAttempt("ip:second");

        // Giây 61: bước dọn chạy và xóa `ip:first`, còn `ip:second` hiệu lực đến giây 90. Bước dọn kế tiếp đến hạn ở giây 121.
        _clock.Advance(TimeSpan.FromSeconds(31));
        limiter.RecordAttempt("ip:third");
        Assert.Equal(2, limiter.TrackedKeyCount);

        // Giây 95: `ip:second` đã hết cửa sổ nhưng chưa đến hạn dọn, nên request này không duyệt bảng và bản ghi cũ còn nguyên.
        _clock.Advance(TimeSpan.FromSeconds(34));
        limiter.RecordAttempt("ip:fourth");
        Assert.Equal(3, limiter.TrackedKeyCount);

        // Giây 121: đến hạn dọn. `ip:second` và `ip:third` đều hết cửa sổ, chỉ còn `ip:fourth` và khóa mới.
        _clock.Advance(TimeSpan.FromSeconds(26));
        limiter.RecordAttempt("ip:fifth");
        Assert.Equal(2, limiter.TrackedKeyCount);
    }
}
