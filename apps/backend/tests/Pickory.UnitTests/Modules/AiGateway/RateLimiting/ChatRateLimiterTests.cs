using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.RateLimiting;
using Pickory.Shared.Exceptions;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.RateLimiting;

// Port từ `rate-limit.test.ts` của bản Node. Hạn mức chat tính theo ngày giờ Việt Nam.
public sealed class ChatRateLimiterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 5, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, "actorKey là bắt buộc")]
    [InlineData("", "actorKey là bắt buộc")]
    [InlineData("invalid_key_123", "actorKey sai định dạng")]
    [InlineData("user:", "định danh sau tiền tố không được để trống")]
    [InlineData("anon:", "định danh sau tiền tố không được để trống")]
    // Chat phải có định danh ổn định. Thiếu hoặc sai định danh thì hạn mức ngày bị bỏ qua.
    public void CheckAndIncrement_RejectsMissingOrMalformedActorKey(string? actorKey, string expectedMessage)
    {
        var limiter = new ChatRateLimiter(new ManualTimeProvider(Noon));

        var error = Assert.Throws<AppException>(() => limiter.CheckAndIncrement(actorKey, 50, 15));

        Assert.Equal("INVALID_ACTOR_KEY", error.Code);
        Assert.Equal(400, error.StatusCode);
        Assert.Contains(expectedMessage, error.Message);
    }

    [Fact]
    // Khách vãng lai có 15 lượt mỗi ngày. Lượt thứ 16 bị chặn với thông báo mời đăng nhập.
    public void CheckAndIncrement_BlocksSixteenthAnonymousMessage()
    {
        var limiter = new ChatRateLimiter(new ManualTimeProvider(Noon));
        for (var count = 1; count <= 15; count++)
        {
            limiter.CheckAndIncrement("anon:guest_device_999", 50, 15);
        }

        var error = Assert.Throws<AiBudgetExceededException>(
            () => limiter.CheckAndIncrement("anon:guest_device_999", 50, 15));

        Assert.Contains("15 lượt trò chuyện miễn phí", error.Message);
    }

    [Fact]
    // Admin đổi hạn mức trong DB. Thông báo lỗi nêu đúng hạn mức mới.
    public void CheckAndIncrement_UsesCustomLimitInMessage()
    {
        var limiter = new ChatRateLimiter(new ManualTimeProvider(Noon));
        for (var count = 1; count <= 5; count++)
        {
            limiter.CheckAndIncrement("anon:custom_limit_guest", 80, 5);
        }

        var error = Assert.Throws<AiBudgetExceededException>(
            () => limiter.CheckAndIncrement("anon:custom_limit_guest", 80, 5));

        Assert.Contains("5 lượt trò chuyện miễn phí", error.Message);
    }

    [Fact]
    // Người đã đăng nhập có 50 lượt mỗi ngày. Lượt thứ 51 bị chặn.
    public void CheckAndIncrement_BlocksFiftyFirstUserMessage()
    {
        var limiter = new ChatRateLimiter(new ManualTimeProvider(Noon));
        for (var count = 1; count <= 50; count++)
        {
            limiter.CheckAndIncrement("user:member_account_123", 50, 15);
        }

        var error = Assert.Throws<AiBudgetExceededException>(
            () => limiter.CheckAndIncrement("user:member_account_123", 50, 15));

        Assert.Contains("50 lượt trò chuyện trong ngày", error.Message);
    }

    [Fact]
    // 16:59 UTC là 23:59 giờ Việt Nam. Hai phút sau đã sang ngày mới, nên bộ đếm về 0.
    public void CheckAndIncrement_ResetsAtVietnamMidnight()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 5, 16, 59, 0, TimeSpan.Zero));
        var limiter = new ChatRateLimiter(time);
        for (var count = 0; count < 15; count++)
        {
            limiter.CheckAndIncrement("anon:visitor_phone_test", 50, 15);
        }

        Assert.Throws<AiBudgetExceededException>(() => limiter.CheckAndIncrement("anon:visitor_phone_test", 50, 15));
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal("2026-10-06", limiter.CheckAndIncrement("anon:visitor_phone_test", 50, 15));
    }

    [Fact]
    // Provider lỗi thì gateway hoàn lại lượt. Người dùng không mất lượt vì sự cố của hệ thống.
    public void Refund_ReturnsTheTurnOfTheSameDay()
    {
        var limiter = new ChatRateLimiter(new ManualTimeProvider(Noon));
        for (var count = 0; count < 15; count++)
        {
            limiter.CheckAndIncrement("anon:refund_guest", 50, 15);
        }

        limiter.Refund("anon:refund_guest", "2026-10-05");

        limiter.CheckAndIncrement("anon:refund_guest", 50, 15);
    }

    [Fact]
    // Lượt lỗi của hôm trước không được trừ vào bộ đếm của ngày mới.
    public void Refund_IgnoresTurnsFromPreviousDay()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 5, 16, 59, 0, TimeSpan.Zero));
        var limiter = new ChatRateLimiter(time);
        var oldDay = limiter.CheckAndIncrement("anon:late_guest", 50, 2);
        time.Advance(TimeSpan.FromMinutes(2));
        limiter.CheckAndIncrement("anon:late_guest", 50, 2);
        limiter.CheckAndIncrement("anon:late_guest", 50, 2);

        limiter.Refund("anon:late_guest", oldDay);

        Assert.Throws<AiBudgetExceededException>(() => limiter.CheckAndIncrement("anon:late_guest", 50, 2));
    }
}
