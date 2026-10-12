using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Checkout;

public sealed class CheckoutPreviewRateLimiterTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));

    private CheckoutPreviewRateLimiter CreateLimiter() => new(_clock);

    private static void ConsumeTimes(CheckoutPreviewRateLimiter limiter, string key, int count)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            limiter.Consume(key);
        }
    }

    [Fact]
    public void Limit_IsTwentyRequestsPerMinute()
    {
        Assert.Equal(20, CheckoutPreviewRateLimiter.MaxRequestsPerWindow);
        Assert.Equal(TimeSpan.FromMinutes(1), CheckoutPreviewRateLimiter.Window);
    }

    [Fact]
    public void Consume_TwentyRequests_AllAllowed()
    {
        var limiter = CreateLimiter();

        var exception = Record.Exception(() => ConsumeTimes(limiter, "ip:1.1.1.1", 20));

        Assert.Null(exception);
    }

    [Fact]
    public void Consume_TwentyFirstRequestInWindow_Throws429()
    {
        var limiter = CreateLimiter();
        ConsumeTimes(limiter, "ip:1.1.1.1", 20);

        var exception = Assert.Throws<AppException>(() => limiter.Consume("ip:1.1.1.1"));

        Assert.Equal(429, exception.StatusCode);
        Assert.Equal("TOO_MANY_REQUESTS", exception.Code);
    }

    [Fact]
    public void Consume_AfterWindowEnds_AllowsRequestsAgain()
    {
        var limiter = CreateLimiter();
        ConsumeTimes(limiter, "ip:1.1.1.1", 20);

        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Throws<AppException>(() => limiter.Consume("ip:1.1.1.1"));

        _clock.Advance(TimeSpan.FromSeconds(1));
        var exception = Record.Exception(() => limiter.Consume("ip:1.1.1.1"));

        Assert.Null(exception);
    }

    [Fact]
    public void Consume_DifferentKeys_HaveSeparateCounters()
    {
        var limiter = CreateLimiter();
        ConsumeTimes(limiter, "ip:1.1.1.1", 20);

        var exception = Record.Exception(() => limiter.Consume("ip:2.2.2.2"));

        Assert.Null(exception);
    }

    [Fact]
    public void Clear_ResetsAllCounters()
    {
        var limiter = CreateLimiter();
        ConsumeTimes(limiter, "ip:1.1.1.1", 20);

        limiter.Clear();

        Assert.Null(Record.Exception(() => limiter.Consume("ip:1.1.1.1")));
    }

    [Fact]
    public void BuildKey_SignedInUser_UsesUserIdAndIgnoresIp()
    {
        Assert.Equal("user:u-1", CheckoutPreviewRateLimiter.BuildKey("u-1", "9.9.9.9"));
    }

    [Fact]
    public void BuildKey_Visitor_UsesClientIp()
    {
        Assert.Equal("ip:9.9.9.9", CheckoutPreviewRateLimiter.BuildKey(null, "9.9.9.9"));
    }
}
