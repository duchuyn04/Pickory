using Pickory.Modules.SupportDesk.Metrics;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Metrics;

// SD-01: hàm thuần tính phân vị theo nearest-rank cho số liệu p50 và p90 của màn quản trị.
public sealed class PercentileTests
{
    // 10 giá trị chưa sắp xếp. Khi sắp tăng dần: 1, 3, 7, 9, 12, 15, 18, 21, 25, 30.
    private static readonly double[] TenUnsortedValues = { 15, 3, 9, 21, 7, 12, 30, 1, 18, 25 };

    [Fact]
    public void Of_EmptyList_ReturnsNull()
    {
        Assert.Null(Percentile.Of(Array.Empty<double>(), 50));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(90)]
    [InlineData(100)]
    public void Of_SingleValue_ReturnsThatValueForAnyPercentile(double percentile)
    {
        Assert.Equal(42d, Percentile.Of(new[] { 42d }, percentile));
    }

    // Tính tay: hạng = ceil(50 / 100 * 10) = 5. Phần tử hạng 5 của danh sách đã sắp là 12.
    [Fact]
    public void Of_P50OfTenValues_ReturnsFifthSmallest()
    {
        Assert.Equal(12d, Percentile.Of(TenUnsortedValues, 50));
    }

    // Tính tay: hạng = ceil(90 / 100 * 10) = 9, nên kết quả là phần tử thứ 9 đã sắp, tức 25.
    // Hạng này là số nguyên, nên hai test p95 và p55 mới kiểm việc làm tròn lên.
    [Fact]
    public void Of_P90OfTenValues_ReturnsNinthSmallest()
    {
        Assert.Equal(25d, Percentile.Of(TenUnsortedValues, 90));
    }

    // Tính tay: hạng = ceil(95 / 100 * 10) = ceil(9,5) = 10, nên kết quả là 30. Làm tròn xuống sẽ ra 25.
    [Fact]
    public void Of_P95OfTenValues_RoundsRankUpToTenth()
    {
        Assert.Equal(30d, Percentile.Of(TenUnsortedValues, 95));
    }

    // Tính tay: hạng = ceil(55 / 100 * 10) = ceil(5,5) = 6, nên kết quả là 15. Làm tròn xuống sẽ ra 12.
    [Fact]
    public void Of_P55OfTenValues_RoundsRankUpToSixth()
    {
        Assert.Equal(15d, Percentile.Of(TenUnsortedValues, 55));
    }

    [Fact]
    public void Of_P100_ReturnsLargestValue()
    {
        Assert.Equal(30d, Percentile.Of(TenUnsortedValues, 100));
    }

    // `double.Epsilon * 10 / 100` tràn xuống 0, nên hạng tính ra là 0. Hàm phải nâng hạng lên 1 thay vì ném lỗi chỉ số mảng.
    [Fact]
    public void Of_SmallestPositivePercentile_ReturnsSmallestValue()
    {
        Assert.Equal(1d, Percentile.Of(TenUnsortedValues, double.Epsilon));
    }

    [Fact]
    public void Of_VerySmallPercentile_ReturnsSmallestValue()
    {
        Assert.Equal(1d, Percentile.Of(TenUnsortedValues, 0.5));
    }

    // Tính tay: 28 * 25 / 100 = 7 đúng, nên hạng là 7. Phép 28 / 100 * 25 cho 7,000000000000001 và bị làm tròn lên thành hạng 8.
    [Fact]
    public void Of_P28OfTwentyFiveValues_ReturnsSeventhSmallest()
    {
        var values = Enumerable.Range(1, 25).Select(number => (double)number).Reverse().ToArray();

        Assert.Equal(7d, Percentile.Of(values, 28));
    }

    [Fact]
    public void Of_DuplicateValues_ReturnsValueAtRank()
    {
        var values = new double[] { 5, 5, 5, 9 };

        Assert.Equal(5d, Percentile.Of(values, 75));
    }

    [Fact]
    public void Of_DoesNotChangeOrderOfInputList()
    {
        // Test khác có thể đổi thứ tự mảng dùng chung, nên test này dùng mảng riêng với thứ tự viết tay.
        var values = new double[] { 15, 3, 9, 21, 7, 12, 30, 1, 18, 25 };

        Percentile.Of(values, 90);

        Assert.Equal(new double[] { 15, 3, 9, 21, 7, 12, 30, 1, 18, 25 }, values);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.1)]
    [InlineData(150)]
    [InlineData(double.NaN)]
    public void Of_PercentileOutsideZeroToHundred_Throws(double percentile)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Percentile.Of(TenUnsortedValues, percentile));

        Assert.Equal("percentile", exception.ParamName);
    }

    // Tham số sai phải báo lỗi cả khi danh sách rỗng, để lỗi gọi hàm không bị che bởi kết quả `null`.
    [Fact]
    public void Of_EmptyListWithInvalidPercentile_StillThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Percentile.Of(Array.Empty<double>(), 0));
    }
}
