using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Addresses;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-04 lát 1 đến 4: bộ đồng bộ ghi danh mục địa chỉ và tắt dòng không còn dùng. Bộ đồng bộ đọc hết trang và giữ dữ liệu cũ khi GHN lỗi.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AddressSyncProcessorTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset SyncMoment = new(2026, 10, 10, 6, 30, 15, TimeSpan.Zero);
    private static readonly DateTime OldSyncedAt = new(2026, 9, 1, 1, 2, 3, DateTimeKind.Utc);

    private readonly FakeTimeProvider _time = new(SyncMoment);
    private readonly ScriptedGhnClient _ghn = new();
    private readonly ListLogger<AddressSyncProcessor> _logger = new();

    public async Task InitializeAsync() => await WithContextAsync(AddressTestDatabase.ClearAsync);

    public async Task DisposeAsync() => await WithContextAsync(AddressTestDatabase.ClearAsync);

    private async Task WithContextAsync(Func<ShippingDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await action(context);
    }

    private async Task<T> ReadAsync<T>(Func<ShippingDbContext, Task<T>> read)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        return await read(context);
    }

    private async Task<AddressSyncResult> SyncAsync()
    {
        // Mỗi lần đồng bộ dùng context mới, giống worker dùng scope mới cho mỗi vòng.
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var processor = new AddressSyncProcessor(context, _ghn, _time, _logger);
        return await processor.SyncAsync(CancellationToken.None);
    }

    private Task<List<AddressProvince>> AllProvincesAsync() =>
        ReadAsync(context => context.AddressProvinces.AsNoTracking().OrderBy(province => province.Code).ToListAsync());

    private Task<List<AddressWard>> AllWardsAsync() =>
        ReadAsync(context => context.AddressWards.AsNoTracking().OrderBy(ward => ward.Code).ToListAsync());

    private Task SeedAsync(IEnumerable<AddressProvince> provinces, IEnumerable<AddressWard> wards) =>
        WithContextAsync(async context =>
        {
            context.AddressProvinces.AddRange(provinces);
            context.AddressWards.AddRange(wards);
            await context.SaveChangesAsync();
        });

    private int ErrorLogCount => _logger.Entries.Count(entry => entry.Level == LogLevel.Error);

    // Lát 1.

    [Fact]
    public async Task Sync_WritesProvincesAndWardsWithSyncedAtFromTimeProvider()
    {
        _ghn.AddProvince(1001, "Hà Nội", wards: [new GhnWard(2001, "Phường Ba Đình", 1001, 1), new GhnWard(2002, "Phường Hoàn Kiếm", 1001, 1)]);
        _ghn.AddProvince(1002, "Hồ Chí Minh", wards: [new GhnWard(2003, "Phường Bến Nghé", 1002, 1)]);

        var result = await SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.ProvinceCount);
        Assert.Equal(3, result.WardCount);
        var provinces = await AllProvincesAsync();
        Assert.Equal(["1001", "1002"], provinces.Select(province => province.Code));
        Assert.Equal(["Hà Nội", "Hồ Chí Minh"], provinces.Select(province => province.Name));
        Assert.All(provinces, province => Assert.True(province.IsActive));
        Assert.All(provinces, province => Assert.Equal(SyncMoment.UtcDateTime, province.SyncedAt));
        var wards = await AllWardsAsync();
        Assert.Equal(["2001", "2002", "2003"], wards.Select(ward => ward.Code));
        Assert.Equal(["1001", "1001", "1002"], wards.Select(ward => ward.ProvinceCode));
        Assert.Equal(["Phường Ba Đình", "Phường Hoàn Kiếm", "Phường Bến Nghé"], wards.Select(ward => ward.Name));
        Assert.All(wards, ward => Assert.Equal(SyncMoment.UtcDateTime, ward.SyncedAt));
    }

    [Fact]
    public async Task Sync_RunTwice_DoesNotCreateDuplicates_AndRefreshesSyncedAt()
    {
        _ghn.AddProvince(1001, "Hà Nội", wards: [new GhnWard(2001, "Phường Ba Đình", 1001, 1)]);

        await SyncAsync();
        var secondMoment = SyncMoment.AddHours(24);
        _time.SetUtcNow(secondMoment);
        var second = await SyncAsync();

        Assert.True(second.Succeeded);
        var provinces = await AllProvincesAsync();
        var wards = await AllWardsAsync();
        Assert.Single(provinces);
        Assert.Single(wards);
        Assert.Equal(secondMoment.UtcDateTime, provinces[0].SyncedAt);
        Assert.Equal(secondMoment.UtcDateTime, wards[0].SyncedAt);
    }

    [Fact]
    public async Task Sync_RenamedUnits_UpdatesNamesOfExistingRows()
    {
        _ghn.AddProvince(1001, "Hà Nội", wards: [new GhnWard(2001, "Phường Ba Đình", 1001, 1)]);
        await SyncAsync();
        _ghn.Provinces.Clear();
        _ghn.AddProvince(1001, "Thủ đô Hà Nội", wards: [new GhnWard(2001, "Phường Ba Đình mới", 1001, 1)]);

        await SyncAsync();

        Assert.Equal("Thủ đô Hà Nội", Assert.Single(await AllProvincesAsync()).Name);
        Assert.Equal("Phường Ba Đình mới", Assert.Single(await AllWardsAsync()).Name);
    }

    [Fact]
    public async Task Sync_WardMovesToAnotherProvince_UpdatesProvinceCode()
    {
        _ghn.AddProvince(1001, "Hà Nội", wards: [new GhnWard(2001, "Phường A", 1001, 1)]);
        _ghn.AddProvince(1002, "Hồ Chí Minh");
        await SyncAsync();
        _ghn.Provinces.Clear();
        _ghn.AddProvince(1001, "Hà Nội");
        // `ParentId` cố ý để sai tỉnh. Mã tỉnh phải lấy từ tỉnh đang đọc.
        _ghn.AddProvince(1002, "Hồ Chí Minh", wards: [new GhnWard(2001, "Phường A", 9999, 1)]);

        await SyncAsync();

        var ward = Assert.Single(await AllWardsAsync());
        Assert.Equal("1002", ward.ProvinceCode);
    }

    // Lát 2.

    [Fact]
    public async Task Sync_StatusNotOne_WritesRowAsInactive()
    {
        _ghn.AddProvince(1001, "Hà Nội", status: 1, wards:
        [
            new GhnWard(2001, "Đang dùng", 1001, 1),
            new GhnWard(2002, "Bị tắt", 1001, 2),
            new GhnWard(2003, "Đã xóa", 1001, 10),
        ]);
        _ghn.AddProvince(1099, "Tỉnh đã giải thể", status: 2);

        await SyncAsync();

        var provinces = await AllProvincesAsync();
        Assert.True(provinces.Single(province => province.Code == "1001").IsActive);
        Assert.False(provinces.Single(province => province.Code == "1099").IsActive);
        var wards = await AllWardsAsync();
        Assert.True(wards.Single(ward => ward.Code == "2001").IsActive);
        Assert.False(wards.Single(ward => ward.Code == "2002").IsActive);
        Assert.False(wards.Single(ward => ward.Code == "2003").IsActive);
    }

    [Fact]
    public async Task Sync_RowGhnNoLongerReturns_IsDeactivatedNotDeleted_AndKeepsSyncedAt()
    {
        await SeedAsync(
            [
                AddressTestDatabase.Province("1001", "Hà Nội", true, OldSyncedAt),
                AddressTestDatabase.Province("1500", "Tỉnh biến mất", true, OldSyncedAt),
            ],
            [
                AddressTestDatabase.Ward("2001", "1001", "Phường còn", true, OldSyncedAt),
                AddressTestDatabase.Ward("2500", "1001", "Phường biến mất", true, OldSyncedAt),
                AddressTestDatabase.Ward("2600", "1500", "Phường của tỉnh biến mất", true, OldSyncedAt),
            ]);
        _ghn.AddProvince(1001, "Hà Nội", wards: [new GhnWard(2001, "Phường còn", 1001, 1)]);

        await SyncAsync();

        var provinces = await AllProvincesAsync();
        Assert.Equal(2, provinces.Count);
        var missingProvince = provinces.Single(province => province.Code == "1500");
        Assert.False(missingProvince.IsActive);
        Assert.Equal(OldSyncedAt, missingProvince.SyncedAt);
        var wards = await AllWardsAsync();
        Assert.Equal(3, wards.Count);
        Assert.False(wards.Single(ward => ward.Code == "2500").IsActive);
        Assert.False(wards.Single(ward => ward.Code == "2600").IsActive);
        Assert.True(wards.Single(ward => ward.Code == "2001").IsActive);
        Assert.Equal(SyncMoment.UtcDateTime, wards.Single(ward => ward.Code == "2001").SyncedAt);
    }

    [Fact]
    public async Task Sync_RowReturnsWithStatusOneAgain_BecomesActiveAgain()
    {
        await SeedAsync(
            [AddressTestDatabase.Province("1001", "Hà Nội", false, OldSyncedAt)],
            [AddressTestDatabase.Ward("2001", "1001", "Phường A", false, OldSyncedAt)]);
        _ghn.AddProvince(1001, "Hà Nội", wards: [new GhnWard(2001, "Phường A", 1001, 1)]);

        await SyncAsync();

        Assert.True(Assert.Single(await AllProvincesAsync()).IsActive);
        Assert.True(Assert.Single(await AllWardsAsync()).IsActive);
    }

    // Lát 3.

    [Fact]
    public async Task Sync_ProvinceWith450Wards_ReadsAllInThreePageCalls()
    {
        _ghn.AddProvince(1001, "Hà Nội", wards: ScriptedGhnClient.CreateWards(1001, 5000, 450));

        var result = await SyncAsync();

        Assert.Equal(450, result.WardCount);
        Assert.Equal(450, (await AllWardsAsync()).Count);
        Assert.Equal(
            [("1001", 0, 200), ("1001", 200, 200), ("1001", 400, 200)],
            _ghn.WardCalls);
    }

    [Fact]
    public async Task Sync_ProvinceWithExactly200Wards_ReadsAll200_AndSecondPageIsEmpty()
    {
        _ghn.AddProvince(1001, "Hà Nội", wards: ScriptedGhnClient.CreateWards(1001, 5000, 200));

        var result = await SyncAsync();

        Assert.Equal(200, result.WardCount);
        Assert.Equal(200, (await AllWardsAsync()).Count);
        Assert.Equal([("1001", 0, 200), ("1001", 200, 200)], _ghn.WardCalls);
    }

    [Fact]
    public async Task Sync_ProvinceWithFewerThan200Wards_ReadsOnePage()
    {
        _ghn.AddProvince(1001, "Hà Nội", wards: ScriptedGhnClient.CreateWards(1001, 5000, 199));
        _ghn.AddProvince(1002, "Hồ Chí Minh");

        await SyncAsync();

        Assert.Equal([("1001", 0, 200), ("1002", 0, 200)], _ghn.WardCalls);
    }

    [Fact]
    public async Task Sync_GhnIgnoresOffsetAndAlwaysReturnsFullPage_StopsAtPageLimit_AsFailure_KeepsOldData()
    {
        await SeedKnownCatalogAsync();
        _ghn.AddProvince(1001, "Tên mới");
        _ghn.AlwaysReturnFullWardPage = true;

        var result = await SyncAsync();

        Assert.False(result.Succeeded);
        // Đúng bằng trần: không ít hơn (đọc chưa đủ) và không nhiều hơn (vòng không dừng).
        Assert.Equal(AddressSyncProcessor.MaxWardPagesPerProvince, _ghn.WardCalls.Count);
        Assert.Contains("1001", result.FailureReason);
        await AssertKnownCatalogUntouchedAsync();
        var error = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("1001", error.Message);
    }

    [Fact]
    public async Task Sync_ProvinceWithStatusNotOne_DoesNotReadItsWards_AndDeactivatesItsOldWards()
    {
        await SeedAsync(
            [
                AddressTestDatabase.Province("1001", "Hà Nội", true, OldSyncedAt),
                AddressTestDatabase.Province("1099", "Tỉnh sắp tắt", true, OldSyncedAt),
            ],
            [
                AddressTestDatabase.Ward("2001", "1001", "Phường còn", true, OldSyncedAt),
                AddressTestDatabase.Ward("2099", "1099", "Phường của tỉnh sắp tắt", true, OldSyncedAt),
            ]);
        _ghn.AddProvince(1001, "Hà Nội", wards: [new GhnWard(2001, "Phường còn", 1001, 1)]);
        _ghn.AddProvince(1099, "Tỉnh sắp tắt", status: 2, wards: [new GhnWard(2099, "Phường của tỉnh sắp tắt", 1099, 1)]);

        var result = await SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.ProvinceCount);
        // Chỉ đọc phường/xã của tỉnh đang dùng. Số phường/xã không gồm phường/xã của tỉnh đã tắt.
        Assert.Equal([("1001", 0, 200)], _ghn.WardCalls);
        Assert.Equal(1, result.WardCount);
        var wards = await AllWardsAsync();
        Assert.True(wards.Single(ward => ward.Code == "2001").IsActive);
        Assert.False(wards.Single(ward => ward.Code == "2099").IsActive);
        Assert.False((await AllProvincesAsync()).Single(province => province.Code == "1099").IsActive);
    }

    [Fact]
    public async Task Sync_ProvinceComesBackWithStatusOne_ReadsAndReactivatesItsWards()
    {
        await SeedAsync(
            [AddressTestDatabase.Province("1099", "Tỉnh quay lại", false, OldSyncedAt)],
            [AddressTestDatabase.Ward("2099", "1099", "Phường quay lại", false, OldSyncedAt)]);
        _ghn.AddProvince(1099, "Tỉnh quay lại", status: 1, wards: [new GhnWard(2099, "Phường quay lại", 1099, 1)]);

        await SyncAsync();

        Assert.True(Assert.Single(await AllProvincesAsync()).IsActive);
        Assert.True(Assert.Single(await AllWardsAsync()).IsActive);
    }

    // Lát 4.

    private async Task SeedKnownCatalogAsync()
    {
        await SeedAsync(
            [AddressTestDatabase.Province("1001", "Tên cũ của tỉnh", true, OldSyncedAt)],
            [AddressTestDatabase.Ward("2001", "1001", "Tên cũ của phường", true, OldSyncedAt)]);
    }

    private async Task AssertKnownCatalogUntouchedAsync()
    {
        var province = Assert.Single(await AllProvincesAsync());
        Assert.Equal("Tên cũ của tỉnh", province.Name);
        Assert.True(province.IsActive);
        Assert.Equal(OldSyncedAt, province.SyncedAt);
        var ward = Assert.Single(await AllWardsAsync());
        Assert.Equal("Tên cũ của phường", ward.Name);
        Assert.True(ward.IsActive);
        Assert.Equal(OldSyncedAt, ward.SyncedAt);
    }

    [Fact]
    public async Task Sync_GhnUnavailableOnProvinceCall_KeepsOldData_AndLogsOneError()
    {
        await SeedKnownCatalogAsync();
        _ghn.AddProvince(1001, "Tên mới", wards: [new GhnWard(2001, "Tên mới", 1001, 1)]);
        _ghn.ProvincesFailure = new ShippingUnavailableException();

        var result = await SyncAsync();

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        await AssertKnownCatalogUntouchedAsync();
        Assert.Equal(1, ErrorLogCount);
    }

    [Fact]
    public async Task Sync_GhnRejectsProvinceCall_KeepsOldData_AndLogsOneError()
    {
        await SeedKnownCatalogAsync();
        _ghn.AddProvince(1001, "Tên mới");
        _ghn.ProvincesFailure = new ShippingRejectedException("Sai cấu hình");

        var result = await SyncAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("Sai cấu hình", result.FailureReason);
        await AssertKnownCatalogUntouchedAsync();
        Assert.Equal(1, ErrorLogCount);
    }

    [Fact]
    public async Task Sync_GhnFailsOnSecondWardPageOfLaterProvince_WritesNothing_NotEvenEarlierProvince()
    {
        await SeedKnownCatalogAsync();
        _ghn.AddProvince(1001, "Tên mới", wards: [new GhnWard(2001, "Tên mới", 1001, 1)]);
        _ghn.AddProvince(1002, "Tỉnh lớn", wards: ScriptedGhnClient.CreateWards(1002, 5000, 450));
        // Lời gọi 1 là tỉnh 1001, lời gọi 2 là trang đầu của tỉnh 1002, lời gọi 3 là trang hai của tỉnh 1002.
        _ghn.FailWardCall(3, new ShippingUnavailableException());

        var result = await SyncAsync();

        Assert.False(result.Succeeded);
        await AssertKnownCatalogUntouchedAsync();
        Assert.Equal(1, ErrorLogCount);
    }

    [Fact]
    public async Task Sync_GhnReturnsEmptyProvinceList_KeepsOldData_AndLogsOneError()
    {
        await SeedKnownCatalogAsync();

        var result = await SyncAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.ProvinceCount);
        await AssertKnownCatalogUntouchedAsync();
        Assert.Equal(1, ErrorLogCount);
    }

    [Fact]
    public async Task Sync_Success_LogsNoError()
    {
        _ghn.AddProvince(1001, "Hà Nội");

        await SyncAsync();

        Assert.Equal(0, ErrorLogCount);
    }
}
