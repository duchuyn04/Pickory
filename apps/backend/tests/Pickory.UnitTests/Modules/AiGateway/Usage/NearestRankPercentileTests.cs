using Pickory.Modules.AiGateway.Usage;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Usage;

// Port phần `calculateNearestRankPercentile` của `usage-summary.test.ts` ở bản Node.
public sealed class NearestRankPercentileTests
{
    [Fact]
    // Chưa có lời gọi nào thì độ trễ phân vị là 0.
    public void Calculate_EmptyListReturnsZero()
    {
        Assert.Equal(0, NearestRankPercentile.Calculate([], 95));
    }

    [Fact]
    // Một phần tử thì mọi phân vị đều là phần tử đó.
    public void Calculate_SingleValue()
    {
        Assert.Equal(250, NearestRankPercentile.Calculate([250], 95));
        Assert.Equal(250, NearestRankPercentile.Calculate([250], 50));
    }

    [Fact]
    // Thứ hạng là `ceil(P / 100 * N)`. Phương pháp này không nội suy, nên kết quả là một độ trễ đo thật.
    public void Calculate_TenValues()
    {
        int[] latencies = [100, 200, 300, 400, 500, 600, 700, 800, 900, 1000];

        Assert.Equal(1000, NearestRankPercentile.Calculate(latencies, 95));
        Assert.Equal(500, NearestRankPercentile.Calculate(latencies, 50));
        Assert.Equal(900, NearestRankPercentile.Calculate(latencies, 90));
    }

    [Fact]
    // 20 phần tử cách đều: p95 có thứ hạng 19.
    public void Calculate_TwentyValues()
    {
        var latencies = Enumerable.Range(1, 20).Select(index => index * 10).ToArray();

        Assert.Equal(190, NearestRankPercentile.Calculate(latencies, 95));
    }

    [Fact]
    // Input chưa sắp xếp vẫn cho đúng kết quả. Hàm tự sắp xếp bản sao, không sửa mảng gốc.
    public void Calculate_SortsCopyOfInput()
    {
        int[] unsorted = [500, 100, 1000, 200, 900, 300, 800, 400, 700, 600];

        Assert.Equal(1000, NearestRankPercentile.Calculate(unsorted, 95));
        Assert.Equal(500, unsorted[0]);
    }
}
