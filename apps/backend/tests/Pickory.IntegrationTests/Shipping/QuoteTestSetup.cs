using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Quotes;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Modules.Shipping.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pickory.Modules.Shipping.Shipments;
using Pickory.Shared.Events;

namespace Pickory.IntegrationTests.Shipping;

// Dữ liệu và cách dựng dịch vụ dùng chung cho các test báo phí của SHIP-05.
internal static class QuoteTestSetup
{
    public static readonly DateTime SyncedAt = new(2026, 10, 10, 6, 30, 15, DateTimeKind.Utc);
    public static readonly Parcel SampleParcel = new(1500, 30, 20, 12);

    // Mã lấy theo danh mục của `FakeGhnClient`, vì bản giả từ chối phường ngoài danh mục như GHN.
    // Phường 1003646 đang dùng, phường 1003649 đã tắt, phường 1004001 thuộc tỉnh 1000002.
    public static readonly ShippingAddress ActiveAddress =
        new("1000001", "Hồ Chí Minh", "1003646", "Phường Bến Nghé", "12 Phố Test");

    public static async Task ResetAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
        DotnetSchemaIntegrationTests.AssertTestConnection(shipping);
        await shipping.Settings.ExecuteDeleteAsync();
        await AddressTestDatabase.ClearAsync(shipping);
    }

    public static async Task SeedCatalogAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        shipping.AddressProvinces.AddRange(
            AddressTestDatabase.Province("1000002", "Hà Nội", isActive: true, SyncedAt),
            AddressTestDatabase.Province("1000001", "Hồ Chí Minh", isActive: true, SyncedAt));
        await shipping.SaveChangesAsync();
        shipping.AddressWards.AddRange(
            AddressTestDatabase.Ward("1004001", "1000002", "Phường Ba Đình", isActive: true, SyncedAt),
            AddressTestDatabase.Ward("1003646", "1000001", "Phường Bến Nghé", isActive: true, SyncedAt),
            AddressTestDatabase.Ward("1003649", "1000001", "Phường đã tắt", isActive: false, SyncedAt));
        await shipping.SaveChangesAsync();
    }

    // Thêm một phường đang dùng vào danh mục đã seed. Test dùng để tạo mã không phải số hoặc phường GHN không biết.
    public static async Task SeedActiveWardAsync(WebApplicationFactory<Program> factory, string wardCode, string provinceCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        shipping.AddressWards.Add(AddressTestDatabase.Ward(wardCode, provinceCode, "Phường thử", isActive: true, SyncedAt));
        await shipping.SaveChangesAsync();
    }

    public static async Task SeedSettingsAsync(WebApplicationFactory<Program> factory, int? defaultServiceTypeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        shipping.Settings.Add(new ShippingSettings
        {
            Id = "singleton",
            SenderName = "Kho Pickory",
            SenderPhone = "0912345678",
            ProvinceCode = "1000002",
            ProvinceName = "Hà Nội",
            WardCode = "1004001",
            WardName = "Phường Ba Đình",
            Line = "12 Phố Kho",
            DefaultServiceTypeId = defaultServiceTypeId,
            UpdatedAt = SyncedAt,
        });
        await shipping.SaveChangesAsync();
    }

    // Dựng dịch vụ với client GHN và bộ phát sự kiện do test chọn. Bộ phát mặc định bỏ qua mọi sự kiện.
    public static ShippingService CreateService(
        IServiceProvider scopedServices,
        IGhnClient ghnClient,
        ILogger<ShippingService> logger,
        IEventDispatcher? eventDispatcher = null)
    {
        // Bộ cập nhật trạng thái dùng cùng bộ phát với dịch vụ, để test thấy mọi sự kiện ở một chỗ.
        var dispatcher = eventDispatcher ?? new RecordingEventDispatcher();
        return new ShippingService(
            scopedServices.GetRequiredService<ShippingSettingsStore>(),
            scopedServices.GetRequiredService<ShippingDbContext>(),
            ghnClient,
            dispatcher,
            TimeProvider.System,
            new ShipmentStatusUpdater(
                scopedServices.GetRequiredService<ShippingDbContext>(),
                dispatcher,
                TimeProvider.System,
                NullLogger<ShipmentStatusUpdater>.Instance),
            logger);
    }

    // Dựng dịch vụ từ scope thật để có `ShippingDbContext` và `ShippingSettingsStore` đúng như ứng dụng.
    public static async Task<Quote> QuoteAsync(
        WebApplicationFactory<Program> factory,
        IGhnClient ghnClient,
        ILogger<ShippingService> logger,
        ShippingAddress? to = null,
        Parcel? parcel = null,
        int codAmount = 0,
        int insuredValue = 0)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = CreateService(scope.ServiceProvider, ghnClient, logger);
        return await service.QuoteAsync(
            to ?? ActiveAddress, parcel ?? SampleParcel, codAmount, insuredValue, CancellationToken.None);
    }
}
