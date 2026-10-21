using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.ActivityTracking.Contracts.Metrics;
using Xunit;

namespace Pickory.IntegrationTests.ActivityTracking;

// ACT-11: `IActivityMetrics` cộng `daily_counters` trong khoảng ngày. Test ghi bộ đếm vào năm 2031 và dùng năm 2035 làm khoảng không có dòng nào, nên kết quả không đổi theo ngày chạy.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ActivityMetricsTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await FunnelSeedData.ResetAsync(database);
    }

    public async Task DisposeAsync()
    {
        await FunnelSeedData.ClearAsync(database);
    }

    [Fact]
    public async Task GetFunnelAsync_SeededRange_ReturnsCountsAndRatesCalculatedByHand()
    {
        var funnel = await GetFunnelAsync(FunnelSeedData.RangeStart, FunnelSeedData.RangeEnd);

        // 120 + 80 + 0 = 200 lượt xem, 30 + 20 = 50 lượt thêm giỏ, 6 + 4 = 10 đơn. Hai ngày ngoài khoảng mỗi chỉ số 1000 không được cộng.
        Assert.Equal(["product_view", "add_to_cart", "order_placed"], funnel.Steps.Select(step => step.Metric));
        Assert.Equal([200L, 50L, 10L], funnel.Steps.Select(step => step.Count));
        Assert.Equal([null, 0.25m, 0.2m], funnel.Steps.Select(step => step.ConversionRate));
    }

    [Fact]
    public async Task GetFunnelAsync_SingleDay_ReturnsOnlyThatDay()
    {
        var funnel = await GetFunnelAsync(FunnelSeedData.RangeMiddle, FunnelSeedData.RangeMiddle);

        Assert.Equal([80L, 20L, 4L], funnel.Steps.Select(step => step.Count));
        Assert.Equal([null, 0.25m, 0.2m], funnel.Steps.Select(step => step.ConversionRate));
    }

    [Fact]
    public async Task GetFunnelAsync_RangeIncludesBothEndDays()
    {
        // Hai ngày kề khoảng mỗi chỉ số 1000. Khoảng rộng thêm một ngày mỗi đầu phải cộng cả hai ngày đó.
        var funnel = await GetFunnelAsync(FunnelSeedData.OutsideBefore, FunnelSeedData.OutsideAfter);

        Assert.Equal([2200L, 2050L, 2010L], funnel.Steps.Select(step => step.Count));
    }

    [Fact]
    public async Task GetFunnelAsync_FirstAndLastDayAlone_AreIncluded()
    {
        var firstDay = await GetFunnelAsync(FunnelSeedData.RangeStart, FunnelSeedData.RangeStart);
        var lastDay = await GetFunnelAsync(FunnelSeedData.OutsideAfter, FunnelSeedData.OutsideAfter);

        // Cận trên và cận dưới đều gồm cận. Ngày `from` và ngày `to` mỗi ngày đứng riêng vẫn có số đếm.
        Assert.Equal([120L, 30L, 6L], firstDay.Steps.Select(step => step.Count));
        Assert.Equal([1000L, 1000L, 1000L], lastDay.Steps.Select(step => step.Count));
    }

    [Fact]
    public async Task GetFunnelAsync_DayWithoutRow_CountsAsZero()
    {
        var funnel = await GetFunnelAsync(FunnelSeedData.RangeEnd, FunnelSeedData.RangeEnd);

        Assert.Equal([0L, 0L, 0L], funnel.Steps.Select(step => step.Count));
    }

    [Fact]
    public async Task GetFunnelAsync_RangeWithoutAnyRow_ReturnsThreeZeroStepsWithNullRates()
    {
        // Năm 2035 không test nào ghi bộ đếm, nên khoảng này chắc chắn không có dòng.
        var funnel = await GetFunnelAsync(new DateOnly(2035, 3, 1), new DateOnly(2035, 3, 5));

        Assert.Equal(["product_view", "add_to_cart", "order_placed"], funnel.Steps.Select(step => step.Metric));
        Assert.Equal([0L, 0L, 0L], funnel.Steps.Select(step => step.Count));
        Assert.Equal([null, null, null], funnel.Steps.Select(step => step.ConversionRate));
    }

    private async Task<Funnel> GetFunnelAsync(DateOnly from, DateOnly to)
    {
        // Mỗi lần gọi mở một scope riêng, giống một request thật dùng `IActivityMetrics` kiểu scoped.
        await using var scope = factory.Services.CreateAsyncScope();
        var metrics = scope.ServiceProvider.GetRequiredService<IActivityMetrics>();
        return await metrics.GetFunnelAsync(new DateRange(from, to), CancellationToken.None);
    }
}
