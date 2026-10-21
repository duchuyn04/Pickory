using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.ActivityTracking.Features.CollectEvents;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.ActivityTracking.CollectEvents;

// ACT-06: mỗi `AnonymousId` được gửi tối đa 60 lượt xem trong 1 phút. Đồng hồ giả giữ test không phụ thuộc giờ thật.
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
            limiter.Consume("anonymous-a");
        }
    }

    [Fact]
    public void Consume_SixtyFirstTimeForSameKey_Throws429()
    {
        var limiter = CreateLimiter();
        for (var i = 0; i < ProductViewRateLimiter.MaxRequestsPerWindow; i++)
        {
            limiter.Consume("anonymous-a");
        }

        var exception = Assert.Throws<AppException>(() => limiter.Consume("anonymous-a"));

        Assert.Equal("TOO_MANY_REQUESTS", exception.Code);
        Assert.Equal(429, exception.StatusCode);
    }

    [Fact]
    public void Consume_DifferentKeyAfterFirstKeyIsLimited_StillAllowed()
    {
        var limiter = CreateLimiter();
        for (var i = 0; i < ProductViewRateLimiter.MaxRequestsPerWindow; i++)
        {
            limiter.Consume("anonymous-a");
        }

        limiter.Consume("anonymous-b");
    }

    [Fact]
    public void Consume_AfterWindowPasses_LimitedKeyIsAllowedAgain()
    {
        var limiter = CreateLimiter();
        for (var i = 0; i < ProductViewRateLimiter.MaxRequestsPerWindow; i++)
        {
            limiter.Consume("anonymous-a");
        }

        _clock.Advance(ProductViewRateLimiter.Window);

        limiter.Consume("anonymous-a");
    }
}
