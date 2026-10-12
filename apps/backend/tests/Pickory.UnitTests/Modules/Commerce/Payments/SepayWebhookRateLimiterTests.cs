using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.Commerce.Payments;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Payments;

public sealed class SepayWebhookRateLimiterTests
{
    [Fact]
    public void Consume_ExhaustedIp_RejectsUntilMinuteBoundaryAndIsolatesOtherIp()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        var limiter = new SepayWebhookRateLimiter(clock);
        // Đồng hồ giả giữ cửa sổ cố định, không phụ thuộc tốc độ máy hoặc thời gian ngủ.
        for (var attempt = 0; attempt < 60; attempt++)
        {
            limiter.Consume("1.1.1.1");
        }
        Assert.Equal(429, Assert.Throws<AppException>(() => limiter.Consume("1.1.1.1")).StatusCode);
        limiter.Consume("2.2.2.2");
        // IP thứ hai có bộ đếm riêng nhưng cũng phải bị chặn khi hết lượt.
        for (var attempt = 1; attempt < 60; attempt++)
        {
            limiter.Consume("2.2.2.2");
        }
        Assert.Equal(429, Assert.Throws<AppException>(() => limiter.Consume("2.2.2.2")).StatusCode);
        // Biên 59 giây chưa mở cửa sổ mới, biên 60 giây phải mở đúng 60 lượt.
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Throws<AppException>(() => limiter.Consume("1.1.1.1"));
        clock.Advance(TimeSpan.FromSeconds(1));
        for (var attempt = 0; attempt < 60; attempt++)
        {
            limiter.Consume("1.1.1.1");
        }
        Assert.Throws<AppException>(() => limiter.Consume("1.1.1.1"));
    }
}
