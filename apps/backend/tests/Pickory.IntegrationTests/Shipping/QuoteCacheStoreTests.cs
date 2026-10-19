using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.FallbackFees;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-14: lưu, đọc và dọn phí GHN đã tính. Dùng DB thật và đồng hồ giả để kiểm mốc 24 giờ chính xác tới giây.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class QuoteCacheStoreTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset SavedMoment = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static readonly QuoteCacheKey BaseKey = new("1003646", "1004001", 2, 1500, 300_000, 0);

    private readonly FakeTimeProvider _time = new(SavedMoment);

    public Task InitializeAsync() => QuoteTestSetup.ResetAsync(factory);

    public Task DisposeAsync() => QuoteTestSetup.ResetAsync(factory);

    // Mỗi lần gọi dùng scope và context riêng, giống mỗi request của API.
    private async Task SaveAsync(QuoteCacheKey key, int fee)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var store = new QuoteCacheStore(scope.ServiceProvider.GetRequiredService<ShippingDbContext>(), _time);
        await store.SaveAsync(key, fee, CancellationToken.None);
    }

    private async Task<int?> FindAsync(QuoteCacheKey key)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var store = new QuoteCacheStore(scope.ServiceProvider.GetRequiredService<ShippingDbContext>(), _time);
        return await store.FindFreshFeeAsync(key, CancellationToken.None);
    }

    private async Task<List<ShippingQuoteCache>> AllRowsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        return await shipping.QuoteCache.AsNoTracking().OrderBy(row => row.SavedAt).ToListAsync();
    }

    [Fact]
    public async Task Find_NothingSaved_ReturnsNull()
    {
        Assert.Null(await FindAsync(BaseKey));
    }

    [Fact]
    public async Task SaveThenFind_ReturnsSavedFee()
    {
        await SaveAsync(BaseKey, 42_000);

        Assert.Equal(42_000, await FindAsync(BaseKey));
    }

    [Fact]
    // Phí còn hạn khi chưa đủ 24 giờ. Đúng 24 giờ là quá hạn, nên so sánh phải là "nhỏ hơn", không phải "nhỏ hơn hoặc bằng".
    public async Task Find_JustUnder24Hours_ReturnsFee_AndExactly24Hours_ReturnsNull()
    {
        await SaveAsync(BaseKey, 42_000);

        _time.Advance(new TimeSpan(23, 59, 59));
        Assert.Equal(42_000, await FindAsync(BaseKey));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await FindAsync(BaseKey));
    }

    [Theory]
    [InlineData("receiverWard")]
    [InlineData("pickupWard")]
    [InlineData("serviceType")]
    [InlineData("chargeableWeight")]
    [InlineData("insuredValue")]
    [InlineData("codAmount")]
    // Phí GHN thay đổi theo từng thành phần của khóa. Đổi một thành phần mà vẫn khớp là trả sai phí.
    public async Task Find_OneKeyComponentDiffers_ReturnsNull(string component)
    {
        await SaveAsync(BaseKey, 42_000);
        var otherKey = component switch
        {
            "receiverWard" => BaseKey with { ReceiverWardCode = "1003647" },
            "pickupWard" => BaseKey with { PickupWardCode = "1004002" },
            "serviceType" => BaseKey with { ServiceTypeId = 5 },
            "chargeableWeight" => BaseKey with { ChargeableWeightGrams = 2000 },
            "insuredValue" => BaseKey with { InsuredValue = 300_001 },
            "codAmount" => BaseKey with { CodAmount = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(component)),
        };

        Assert.Null(await FindAsync(otherKey));
        // Khóa gốc vẫn còn phí, chứng minh dòng không bị mất mà chỉ không khớp.
        Assert.Equal(42_000, await FindAsync(BaseKey));
    }

    [Fact]
    public async Task Save_SameKeyTwice_KeepsOneRowWithNewFeeAndNewTime()
    {
        await SaveAsync(BaseKey, 42_000);
        _time.Advance(TimeSpan.FromHours(2));
        await SaveAsync(BaseKey, 51_000);

        var rows = await AllRowsAsync();

        var row = Assert.Single(rows);
        Assert.Equal(51_000, row.Fee);
        Assert.Equal(SavedMoment.UtcDateTime.AddHours(2), row.SavedAt);
    }

    [Fact]
    // Nhiều request cùng khóa chạy cùng lúc không được làm lỗi báo phí. `ON CONFLICT` trong DB quyết định, không phải thứ tự lập lịch luồng.
    public async Task Save_ManyParallelRequestsWithSameKey_DoNotThrowAndKeepOneRow()
    {
        var saves = Enumerable.Range(0, 12)
            .Select(index => Task.Run(() => SaveAsync(BaseKey, 40_000 + index)))
            .ToArray();

        await Task.WhenAll(saves);

        var row = Assert.Single(await AllRowsAsync());
        Assert.InRange(row.Fee, 40_000, 40_011);
    }

    [Fact]
    // Mỗi lần lưu xóa dòng từ 24 giờ trở lên. Dòng 23 giờ 59 phút 59 giây tuổi vẫn còn.
    public async Task Save_DeletesRowsOlderThanOrExactly24Hours_KeepsFreshRows()
    {
        var olderThanDay = BaseKey with { ReceiverWardCode = "old-25h" };
        var exactlyDay = BaseKey with { ReceiverWardCode = "old-24h" };
        var justUnderDay = BaseKey with { ReceiverWardCode = "fresh-23h59m59s" };
        await SaveAsync(olderThanDay, 10_000);
        _time.Advance(TimeSpan.FromHours(1));
        await SaveAsync(exactlyDay, 20_000);
        _time.Advance(TimeSpan.FromSeconds(1));
        await SaveAsync(justUnderDay, 30_000);
        // Tới giờ thứ 25: dòng đầu 25 giờ tuổi, dòng thứ hai đúng 24 giờ, dòng thứ ba 23 giờ 59 phút 59 giây.
        _time.Advance(new TimeSpan(23, 59, 59));
        var trigger = BaseKey with { ReceiverWardCode = "trigger" };

        await SaveAsync(trigger, 40_000);

        var remaining = (await AllRowsAsync()).Select(row => row.ReceiverWardCode).Order().ToArray();
        Assert.Equal(["fresh-23h59m59s", "trigger"], remaining);
    }
}
