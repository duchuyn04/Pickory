using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.ActivityTracking.Features.CollectEvents;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.ActivityTracking.CollectEvents;

// ACT-06 và ACT-14: mỗi khóa (`UserId` hoặc IP) được gửi tối đa 60 lượt xem trong 1 phút. Đồng hồ giả giữ test không phụ thuộc giờ thật.
public sealed class ProductViewRateLimiterTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2031, 3, 10, 9, 0, 0, TimeSpan.Zero));

    private ProductViewRateLimiter CreateLimiter() => new(_clock);

    [Fact]
    public void Limits_AreSixtyRequestsPerOneMinute()
    {
        Assert.Equal(60, ProductViewRateLimiter.MaxRequestsPerWindow);
        Assert.Equal(TimeSpan.FromMinutes(1), ProductViewRateLimiter.Window);
    }

    [Fact]
    public void Consume_UpToSixtyTimesForSameKey_Allowed()
    {
        var limiter = CreateLimiter();

        for (var i = 0; i < ProductViewRateLimiter.MaxRequestsPerWindow; i++)
        {
            limiter.Consume("ip:203.0.113.7");
        }
    }

    [Fact]
    public void Consume_SixtyFirstTimeForSameKey_Throws429()
    {
        var limiter = CreateLimiter();
        for (var i = 0; i < ProductViewRateLimiter.MaxRequestsPerWindow; i++)
        {
            limiter.Consume("ip:203.0.113.7");
        }

        var exception = Assert.Throws<AppException>(() => limiter.Consume("ip:203.0.113.7"));

        Assert.Equal("TOO_MANY_REQUESTS", exception.Code);
        Assert.Equal(429, exception.StatusCode);
    }

    [Fact]
    public void Consume_DifferentKeyAfterFirstKeyIsLimited_StillAllowed()
    {
        var limiter = CreateLimiter();
        for (var i = 0; i < ProductViewRateLimiter.MaxRequestsPerWindow; i++)
        {
            limiter.Consume("ip:203.0.113.7");
        }

        limiter.Consume("ip:198.51.100.9");
    }

    [Fact]
    public void Consume_AfterWindowPasses_LimitedKeyIsAllowedAgain()
    {
        var limiter = CreateLimiter();
        for (var i = 0; i < ProductViewRateLimiter.MaxRequestsPerWindow; i++)
        {
            limiter.Consume("ip:203.0.113.7");
        }

        _clock.Advance(ProductViewRateLimiter.Window);

        limiter.Consume("ip:203.0.113.7");
    }

    [Fact]
    public void BuildKey_WithUserId_ReturnsUserKey()
    {
        // Khách đã đăng nhập đi theo `UserId`, nên đổi IP hay cookie không đổi hạn mức.
        Assert.Equal("user:user-1", ProductViewRateLimiter.BuildKey("user-1", "203.0.113.7"));
    }

    [Fact]
    public void BuildKey_WithoutUserId_ReturnsIpKey()
    {
        // Khách vãng lai đi theo IP, vì đổi `pk_anon` ở mỗi request không được đổi hạn mức.
        Assert.Equal("ip:203.0.113.7", ProductViewRateLimiter.BuildKey(null, "203.0.113.7"));
    }

    [Fact]
    public void BuildKey_SameUserIdFromDifferentIps_ReturnsSameKey()
    {
        Assert.Equal(
            ProductViewRateLimiter.BuildKey("user-1", "203.0.113.7"),
            ProductViewRateLimiter.BuildKey("user-1", "198.51.100.9"));
    }
}
