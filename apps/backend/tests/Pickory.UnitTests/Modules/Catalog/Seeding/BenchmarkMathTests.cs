using Pickory.Modules.Catalog.Seeding;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Seeding;

// CAT-12: phép tính phân vị, kiểm tên DB benchmark và danh sách truy vấn đo.
public sealed class BenchmarkMathTests
{
    [Fact]
    public void Percentile_UsesNearestRank()
    {
        // 100 giá trị từ 1 đến 100: hạng của p95 là 95, nên giá trị là 95 (không phải 96 hay 94).
        var values = Enumerable.Range(1, 100).Select(number => (double)number).ToList();

        Assert.Equal(95, LatencyStatistics.Percentile(values, 95));
        Assert.Equal(50, LatencyStatistics.Percentile(values, 50));
        Assert.Equal(100, LatencyStatistics.Percentile(values, 100));
    }

    [Fact]
    public void Percentile_RoundsTheRankUp()
    {
        // 10 giá trị: hạng p95 là ceil(9.5) = 10, tức giá trị lớn nhất.
        var values = Enumerable.Range(1, 10).Select(number => (double)number).ToList();

        Assert.Equal(10, LatencyStatistics.Percentile(values, 95));
        Assert.Equal(5, LatencyStatistics.Percentile(values, 50));
    }

    [Fact]
    public void Percentile_DoesNotRequireSortedInput()
    {
        Assert.Equal(3, LatencyStatistics.Percentile([5, 1, 3, 2, 4], 60));
    }

    [Fact]
    public void Percentile_OfOneValue_ReturnsThatValue()
    {
        Assert.Equal(7, LatencyStatistics.Percentile([7], 95));
    }

    [Fact]
    public void Percentile_OfEmptyList_Throws()
    {
        Assert.Throws<ArgumentException>(() => LatencyStatistics.Percentile([], 95));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Percentile_OutOfRange_Throws(double percentile)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LatencyStatistics.Percentile([1, 2, 3], percentile));
    }

    [Fact]
    public void Summarize_ReportsCountMedianP95AndMax()
    {
        var values = Enumerable.Range(1, 200).Select(number => (double)number).ToList();

        var summary = LatencyStatistics.Summarize(values);

        Assert.Equal(200, summary.Count);
        Assert.Equal(100, summary.P50);
        Assert.Equal(190, summary.P95);
        Assert.Equal(200, summary.Max);
    }

    [Fact]
    public void IsWithinBudget_RequiresP95BelowTheLimit()
    {
        var below = new LatencySummary(300, 20, 299.9, 400);
        var equal = new LatencySummary(300, 20, 300, 400);

        Assert.True(below.IsP95Below(300));
        Assert.False(equal.IsP95Below(300));
    }

    [Theory]
    [InlineData("postgresql://pickory:pickory@localhost:5432/pickory_bench")]
    [InlineData("postgres://u:p@127.0.0.1:5432/PICKORY_BENCH")]
    [InlineData("postgresql://u:p@localhost/bench")]
    public void EnsureBenchmarkDatabase_AcceptsNamesContainingBench(string url)
    {
        BenchmarkDatabase.EnsureBenchmarkDatabase(url);
    }

    [Theory]
    [InlineData("postgresql://pickory:pickory@localhost:5432/pickory")]
    [InlineData("postgresql://pickory:pickory@localhost:5432/pickory_test")]
    [InlineData("postgresql://pickory:pickory@localhost:5432/pickory_e2e")]
    [InlineData("postgresql://pickory:pickory@bench:5432/pickory")]
    public void EnsureBenchmarkDatabase_RejectsOtherDatabases(string url)
    {
        var error = Assert.Throws<InvalidOperationException>(() => BenchmarkDatabase.EnsureBenchmarkDatabase(url));

        Assert.Contains("bench", error.Message);
    }

    [Fact]
    public void EnsureBenchmarkDatabase_RejectsEmptyUrl()
    {
        Assert.ThrowsAny<Exception>(() => BenchmarkDatabase.EnsureBenchmarkDatabase(""));
    }

    [Fact]
    public void BuildPaths_CoversEveryKindOfQuery()
    {
        var paths = BenchmarkQueries.BuildPaths(["laptop", "dien-thoai"], ["asus", "apple"], "attr.ram_gb=16");

        Assert.Contains("/api/products", paths);
        Assert.Contains("/api/products?category=laptop", paths);
        Assert.Contains(paths, path => path.Contains("brand=asus"));
        Assert.Contains(paths, path => path.Contains("priceMin=") && path.Contains("priceMax="));
        Assert.Contains(paths, path => path.Contains("attr.ram_gb=16"));
        Assert.Contains(paths, path => path.Contains("sort=price_asc"));
        Assert.Contains(paths, path => path.Contains("sort=price_desc"));
        Assert.Contains(paths, path => path.Contains("page=2"));
        Assert.Equal(paths.Count, paths.Distinct().Count());
    }

    [Fact]
    public void BuildPaths_EveryPathStartsWithTheProductsRoute()
    {
        var paths = BenchmarkQueries.BuildPaths(["laptop"], ["asus"], "attr.ram_gb=16");

        Assert.All(paths, path => Assert.StartsWith("/api/products", path));
    }

    [Fact]
    public void Repeat_CyclesThroughThePathsToTheRequestedCount()
    {
        var sequence = BenchmarkQueries.Repeat(["a", "b", "c"], 7).ToList();

        Assert.Equal(["a", "b", "c", "a", "b", "c", "a"], sequence);
    }
}
