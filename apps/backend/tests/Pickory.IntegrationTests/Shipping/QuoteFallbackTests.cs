using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Quotes;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.FallbackFees;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-14: khi GHN không phản hồi lúc báo phí, `QuoteAsync` dùng phí đã lưu còn hạn rồi bảng phí dự phòng.
// Lỗi GHN từ chối và lỗi cấu hình giữ nguyên.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class QuoteFallbackTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset StartMoment = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    // Địa chỉ lấy hàng seed là Hà Nội (miền Bắc), phường 1004001. Địa chỉ nhận: cùng tỉnh, khác tỉnh cùng miền (Hải Phòng), khác miền (Hồ Chí Minh).
    private static readonly ShippingAddress SameProvinceAddress =
        new("1000000", "Hà Nội", "1004001", "Phường Ba Đình", "12 Phố Test");
    private static readonly ShippingAddress SameRegionAddress =
        new("1000004", "Hải Phòng", "1004101", "Phường Hồng Bàng", "12 Phố Test");
    private static readonly ShippingAddress CrossRegionAddress = QuoteTestSetup.ActiveAddress;
    // Mã tỉnh không có trong bảng vùng và khác tỉnh lấy hàng.
    private static readonly ShippingAddress UnknownRegionAddress =
        new("1000098", "Tỉnh thử", "1004201", "Phường thử", "12 Phố Test");

    private readonly FakeGhnClient _fake = new();
    private readonly FakeTimeProvider _time = new(StartMoment);
    private readonly ListLogger<ShippingService> _logger = new();

    public async Task InitializeAsync()
    {
        await QuoteTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await SeedExtraAddressesAsync();
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: null);
    }

    public Task DisposeAsync() => QuoteTestSetup.ResetAsync(factory);

    private async Task SeedExtraAddressesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        shipping.AddressProvinces.AddRange(
            AddressTestDatabase.Province("1000004", "Hải Phòng", isActive: true, QuoteTestSetup.SyncedAt),
            AddressTestDatabase.Province("1000098", "Tỉnh thử", isActive: true, QuoteTestSetup.SyncedAt));
        await shipping.SaveChangesAsync();
        shipping.AddressWards.AddRange(
            AddressTestDatabase.Ward("1004101", "1000004", "Phường Hồng Bàng", isActive: true, QuoteTestSetup.SyncedAt),
            AddressTestDatabase.Ward("1004201", "1000098", "Phường thử", isActive: true, QuoteTestSetup.SyncedAt));
        await shipping.SaveChangesAsync();
    }

    // Dòng bật với đủ chín giá. Gói 1500 g của `SampleParcel`: nội tỉnh 25.000 đ, nội vùng 36.000 đ, liên vùng 47.000 đ.
    private async Task SeedFallbackAsync(bool enabled)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        shipping.FallbackFees.Add(new ShippingFallbackFee
        {
            Id = FallbackFeeStore.SingletonId,
            Enabled = enabled,
            SameProvinceBaseFee = 20_000,
            SameProvinceBaseWeightGrams = 1000,
            SameProvinceStepFee = 5_000,
            SameRegionBaseFee = 30_000,
            SameRegionBaseWeightGrams = 1000,
            SameRegionStepFee = 6_000,
            CrossRegionBaseFee = 40_000,
            CrossRegionBaseWeightGrams = 1000,
            CrossRegionStepFee = 7_000,
            UpdatedAt = QuoteTestSetup.SyncedAt,
        });
        await shipping.SaveChangesAsync();
    }

    private async Task<List<ShippingQuoteCache>> CacheRowsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        return await shipping.QuoteCache.AsNoTracking().ToListAsync();
    }

    // Dịch vụ dùng đồng hồ giả cho phí đã lưu, để test đặt được mốc 24 giờ.
    private Task<Quote> QuoteAsync(ShippingAddress? to = null, int codAmount = 0, int insuredValue = 0) =>
        QuoteTestSetup.QuoteAsync(
            factory, _fake, _logger, to, codAmount: codAmount, insuredValue: insuredValue,
            createQuoteCache: services => new QuoteCacheStore(services.GetRequiredService<ShippingDbContext>(), _time));

    private void GhnIsDown() => _fake.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Unavailable);

    // Log cảnh báo của lần dùng phí thay thế, không tính các cảnh báo khác như ngày giao lỗi.
    private string[] SubstituteWarnings() => _logger.Entries
        .Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("GHN không phản hồi khi báo phí", StringComparison.Ordinal))
        .Select(entry => entry.Message)
        .ToArray();

    [Fact]
    public async Task Quote_GhnSucceeds_ReturnsGhnSourceAndSavesTheFee()
    {
        var quote = await QuoteAsync();

        Assert.Equal(QuoteSource.Ghn, quote.Source);
        var row = Assert.Single(await CacheRowsAsync());
        Assert.Equal(quote.Fee, row.Fee);
        // Khóa gồm phường nhận, phường lấy hàng, gói dịch vụ, khối lượng tính cước, giá trị khai giá và tiền thu hộ.
        Assert.Equal("1003646", row.ReceiverWardCode);
        Assert.Equal("1004001", row.PickupWardCode);
        Assert.Equal(QuoteRules.LightGoodsServiceTypeId, row.ServiceTypeId);
        Assert.Equal(1500, row.ChargeableWeightGrams);
        Assert.Equal(0, row.InsuredValue);
        Assert.Equal(0, row.CodAmount);
        Assert.Equal(StartMoment.UtcDateTime, row.SavedAt);
    }

    [Fact]
    // Giá trị khai giá vượt trần được kẹp trước khi vào khóa, giống giá trị gửi GHN.
    public async Task Quote_GhnSucceeds_SavesClampedInsuredValueAndCodAmountInKey()
    {
        await QuoteAsync(codAmount: 700_000, insuredValue: 9_000_000);

        var row = Assert.Single(await CacheRowsAsync());
        Assert.Equal(QuoteRules.MaxInsuredValue, row.InsuredValue);
        Assert.Equal(700_000, row.CodAmount);
    }

    [Fact]
    public async Task Quote_GhnDownAndFreshCachedFee_ReturnsCachedFeeWithoutDeliveryDate()
    {
        var first = await QuoteAsync();
        Assert.NotNull(first.ExpectedDeliveryDate);
        GhnIsDown();

        var second = await QuoteAsync();

        Assert.Equal(QuoteSource.Cached, second.Source);
        Assert.Equal(first.Fee, second.Fee);
        Assert.Equal(first.ServiceTypeId, second.ServiceTypeId);
        Assert.Null(second.ExpectedDeliveryDate);
    }

    [Fact]
    // Phí đã lưu được ưu tiên hơn bảng phí dự phòng, kể cả khi bảng đang bật và cho số khác.
    public async Task Quote_GhnDownWithCachedFeeAndEnabledTable_PrefersCachedFee()
    {
        await SeedFallbackAsync(enabled: true);
        var first = await QuoteAsync();
        GhnIsDown();

        var second = await QuoteAsync();

        Assert.Equal(QuoteSource.Cached, second.Source);
        Assert.Equal(first.Fee, second.Fee);
        Assert.NotEqual(47_000, second.Fee);
    }

    [Fact]
    // Còn hạn tới giây cuối của giờ thứ 24. Từ giây thứ 24 giờ 0 phút 0 giây dùng bảng phí dự phòng.
    public async Task Quote_GhnDown_CachedFeeExpiresAt24Hours_ThenFallbackTableApplies()
    {
        await SeedFallbackAsync(enabled: true);
        await QuoteAsync();
        GhnIsDown();

        _time.Advance(new TimeSpan(23, 59, 59));
        var stillFresh = await QuoteAsync();
        _time.Advance(TimeSpan.FromSeconds(1));
        var expired = await QuoteAsync();

        Assert.Equal(QuoteSource.Cached, stillFresh.Source);
        Assert.Equal(QuoteSource.Fallback, expired.Source);
        Assert.Equal(47_000, expired.Fee);
        Assert.Null(expired.ExpectedDeliveryDate);
    }

    [Fact]
    public async Task Quote_GhnDownAndCachedFeeExpiredWithoutTable_ThrowsUnavailable()
    {
        await QuoteAsync();
        GhnIsDown();
        _time.Advance(TimeSpan.FromHours(24));

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync());
    }

    [Fact]
    public async Task Quote_GhnDownNoCachedFeeAndSameProvince_ReturnsFallbackFeeOfSameProvinceTier()
    {
        await SeedFallbackAsync(enabled: true);
        GhnIsDown();

        var quote = await QuoteAsync(SameProvinceAddress);

        Assert.Equal(QuoteSource.Fallback, quote.Source);
        Assert.Equal(25_000, quote.Fee);
        Assert.Null(quote.ExpectedDeliveryDate);
    }

    [Fact]
    public async Task Quote_GhnDownNoCachedFeeAndSameRegion_ReturnsFallbackFeeOfSameRegionTier()
    {
        await SeedFallbackAsync(enabled: true);
        GhnIsDown();

        var quote = await QuoteAsync(SameRegionAddress);

        Assert.Equal(QuoteSource.Fallback, quote.Source);
        Assert.Equal(36_000, quote.Fee);
        Assert.Null(quote.ExpectedDeliveryDate);
    }

    [Fact]
    public async Task Quote_GhnDownNoCachedFeeAndCrossRegion_ReturnsFallbackFeeOfCrossRegionTier()
    {
        await SeedFallbackAsync(enabled: true);
        GhnIsDown();

        var quote = await QuoteAsync(CrossRegionAddress);

        Assert.Equal(QuoteSource.Fallback, quote.Source);
        Assert.Equal(47_000, quote.Fee);
        Assert.Null(quote.ExpectedDeliveryDate);
    }

    [Fact]
    // Admin chọn gói hàng nặng thì phí dự phòng vẫn trả đúng gói đó, vì khách đã thấy gói này lúc chọn.
    public async Task Quote_GhnDownWithHeavyGoodsSetting_FallbackKeepsConfiguredServiceType()
    {
        await QuoteTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: QuoteRules.HeavyGoodsServiceTypeId);
        await SeedFallbackAsync(enabled: true);
        GhnIsDown();

        var quote = await QuoteAsync();

        Assert.Equal(QuoteSource.Fallback, quote.Source);
        Assert.Equal(QuoteRules.HeavyGoodsServiceTypeId, quote.ServiceTypeId);
    }

    [Fact]
    public async Task Quote_GhnDownAndTableDisabled_ThrowsUnavailable()
    {
        await SeedFallbackAsync(enabled: false);
        GhnIsDown();

        var exception = await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync());

        Assert.Equal("SHIPPING_UNAVAILABLE", exception.Code);
    }

    [Fact]
    public async Task Quote_GhnDownAndNoTableRow_ThrowsUnavailable()
    {
        GhnIsDown();

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync());
    }

    [Fact]
    // Tỉnh nhận không có trong bảng vùng và khác tỉnh lấy hàng thì không xác định được tuyến, nên không đoán phí.
    public async Task Quote_GhnDownAndReceiverProvinceOutsideRegionTable_ThrowsUnavailable()
    {
        await SeedFallbackAsync(enabled: true);
        GhnIsDown();

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync(UnknownRegionAddress));
    }

    [Fact]
    // GHN đã trả lời và từ chối, nên không dùng phí đã lưu hay phí dự phòng dù cả hai đều có.
    public async Task Quote_GhnRejects_ThrowsRejectedEvenWithCachedFeeAndEnabledTable()
    {
        await SeedFallbackAsync(enabled: true);
        await QuoteAsync();
        _fake.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Rejected);

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => QuoteAsync());

        Assert.Equal("SHIPPING_REJECTED", exception.Code);
        Assert.Empty(SubstituteWarnings());
    }

    [Fact]
    // Phường lấy hàng đổi thì khóa đổi theo. Phí đã lưu theo địa chỉ cũ không còn dùng được.
    public async Task Quote_GhnDownAfterPickupWardChanged_DoesNotUseCachedFee()
    {
        await QuoteAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            await shipping.Settings.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.WardCode, "1004002"));
        }
        GhnIsDown();

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync());
    }

    [Fact]
    // Mỗi thành phần của khóa đều quyết định phí. Phí đã lưu cho kiện khác không được dùng cho kiện này.
    public async Task Quote_GhnDownWithDifferentParcelCodOrInsurance_DoesNotUseCachedFee()
    {
        await QuoteAsync();
        GhnIsDown();

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync(codAmount: 1));
        await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync(insuredValue: 1));
    }

    [Fact]
    // Lưu phí lỗi (DB lỗi) chỉ ghi cảnh báo. Khách vẫn nhận phí GHN vừa tính.
    public async Task Quote_SavingTheFeeFails_StillReturnsGhnFeeAndLogsWarning()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<ShippingDbContext>().Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<ShippingDbContext>();
        ShippingDbContext.ConfigureOptions(options, connectionString);
        options.AddInterceptors(new FailOnceCommandInterceptor("INSERT INTO shipping_quote_cache", "DB lưu phí không khả dụng"));
        await using var failingContext = new ShippingDbContext(options.Options);

        var quote = await QuoteTestSetup.QuoteAsync(
            factory, _fake, _logger, createQuoteCache: _ => new QuoteCacheStore(failingContext, _time));

        Assert.Equal(QuoteSource.Ghn, quote.Source);
        Assert.True(quote.Fee > 0);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Không lưu được phí GHN", StringComparison.Ordinal));
        Assert.Empty(await CacheRowsAsync());
    }

    [Fact]
    // DB lỗi lúc tìm phí thay thế không được thành lỗi 500 che mất lỗi gốc. Khách vẫn nhận `SHIPPING_UNAVAILABLE`.
    public async Task Quote_GhnDownAndReadingCachedFeeFails_ThrowsUnavailableAndLogsWarning()
    {
        await QuoteAsync();
        GhnIsDown();
        await using var scope = factory.Services.CreateAsyncScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<ShippingDbContext>().Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<ShippingDbContext>();
        ShippingDbContext.ConfigureOptions(options, connectionString);
        options.AddInterceptors(new FailOnceCommandInterceptor("SELECT", "DB đọc phí không khả dụng"));
        await using var failingContext = new ShippingDbContext(options.Options);

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteTestSetup.QuoteAsync(
            factory, _fake, _logger, createQuoteCache: _ => new QuoteCacheStore(failingContext, _time)));

        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Không đọc được phí đã lưu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Quote_GhnDownUsingCachedFee_LogsOneWarningWithoutAddressData()
    {
        await QuoteAsync();
        GhnIsDown();

        await QuoteAsync();

        AssertWarningHasOnlyAllowedFacts(SubstituteWarnings(), "Cached");
    }

    [Fact]
    public async Task Quote_GhnDownUsingFallbackTable_LogsOneWarningWithoutAddressData()
    {
        await SeedFallbackAsync(enabled: true);
        GhnIsDown();

        await QuoteAsync();

        AssertWarningHasOnlyAllowedFacts(SubstituteWarnings(), "Fallback");
    }

    // Log chỉ có nguồn phí, tuyến và khối lượng tính cước. Không có mã phường, tên phường, dòng địa chỉ hay số điện thoại.
    private static void AssertWarningHasOnlyAllowedFacts(string[] warnings, string expectedSource)
    {
        var message = Assert.Single(warnings);
        Assert.Contains(expectedSource, message, StringComparison.Ordinal);
        Assert.Contains("CrossRegion", message, StringComparison.Ordinal);
        Assert.Contains("1500", message, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "1003646", "1004001", "Bến Nghé", "Ba Đình", "12 Phố", "Phố Kho", "0912345678", "Kho Pickory" })
        {
            Assert.DoesNotContain(forbidden, message, StringComparison.Ordinal);
        }
    }
}
