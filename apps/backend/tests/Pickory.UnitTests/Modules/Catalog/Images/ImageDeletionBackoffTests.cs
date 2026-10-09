using Pickory.Modules.Catalog.Images;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Images;

// Khoảng chờ tăng dần để lỗi tạm thời của Cloudinary không bị gọi lại liên tục.
public sealed class ImageDeletionBackoffTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(7, 64)]
    // Mỗi lần lỗi nhân đôi khoảng chờ, bắt đầu từ 1 phút.
    public void DelayAfter_DoublesFromOneMinute(int failedAttempts, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), ImageDeletionBackoff.DelayAfter(failedAttempts));
    }

    [Fact]
    // Khoảng chờ có trần 6 giờ, để job vẫn được thử lại trong ngày.
    public void DelayAfter_CapsAtSixHours()
    {
        Assert.Equal(TimeSpan.FromHours(6), ImageDeletionBackoff.DelayAfter(30));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    // Chưa lỗi lần nào thì không có khoảng chờ hợp lệ. Gọi sai là lỗi lập trình.
    public void DelayAfter_RejectsNonPositiveAttempts(int failedAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageDeletionBackoff.DelayAfter(failedAttempts));
    }
}
