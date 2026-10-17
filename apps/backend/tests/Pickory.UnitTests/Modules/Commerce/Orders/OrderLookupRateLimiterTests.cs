using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.Commerce.Orders.Lookup;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

// Kiểm tra giới hạn tần suất tra cứu đơn hàng theo địa chỉ IP và theo email.
public sealed class OrderLookupRateLimiterTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));

    private OrderLookupRateLimiter CreateLimiter() => new(_clock);

    [Fact]
    // Tối đa 5 lần gọi hợp lệ trong một cửa sổ 15 phút.
    public void CheckAndConsume_UpToFiveTimes_Allowed()
    {
        var limiter = CreateLimiter();

        for (var i = 0; i < 5; i++)
        {
            limiter.CheckAndConsume("127.0.0.1", "khach@example.com");
        }
    }

    [Fact]
    // Lần gọi thứ 6 từ cùng IP bị chặn và ném ngoại lệ 429.
    public void CheckAndConsume_SixthAttemptSameIpDifferentEmails_Throws429()
    {
        var limiter = CreateLimiter();

        for (var i = 1; i <= 5; i++)
        {
            limiter.CheckAndConsume("127.0.0.1", $"khach{i}@example.com");
        }

        var exception = Assert.Throws<AppException>(() => limiter.CheckAndConsume("127.0.0.1", "khach6@example.com"));

        Assert.Equal("TOO_MANY_REQUESTS", exception.Code);
        Assert.Equal(429, exception.StatusCode);
    }

    [Fact]
    // Lần gọi thứ 6 cho cùng email bị chặn và ném ngoại lệ 429.
    public void CheckAndConsume_SixthAttemptSameEmailDifferentIps_Throws429()
    {
        var limiter = CreateLimiter();

        for (var i = 1; i <= 5; i++)
        {
            limiter.CheckAndConsume($"192.168.1.{i}", "khach@example.com");
        }

        var exception = Assert.Throws<AppException>(() => limiter.CheckAndConsume("192.168.1.6", "khach@example.com"));

        Assert.Equal("TOO_MANY_REQUESTS", exception.Code);
        Assert.Equal(429, exception.StatusCode);
    }

    [Fact]
    // Sau khi hết cửa sổ 15 phút thì cho phép gọi lại bình thường.
    public void CheckAndConsume_AfterWindowEnds_AllowsRequestsAgain()
    {
        var limiter = CreateLimiter();

        for (var i = 0; i < 5; i++)
        {
            limiter.CheckAndConsume("127.0.0.1", "khach@example.com");
        }

        _clock.Advance(TimeSpan.FromMinutes(15));

        limiter.CheckAndConsume("127.0.0.1", "khach@example.com");
    }

    [Fact]
    // Lệnh xóa bộ đếm đặt lại mọi hạn mức cho các lần kiểm thử kế tiếp.
    public void Clear_ResetsAllCounters()
    {
        var limiter = CreateLimiter();

        for (var i = 0; i < 5; i++)
        {
            limiter.CheckAndConsume("127.0.0.1", "khach@example.com");
        }

        limiter.Clear();

        limiter.CheckAndConsume("127.0.0.1", "khach@example.com");
    }
}
