using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Shared.Configuration;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping;

// SHIP-05 lát 1: `commerce` lấy `IShippingService` từ DI và nhận lỗi chưa cấu hình có mã ổn định.
public sealed class ShippingServiceRegistrationTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    [Fact]
    public void AddShippingModule_RegistersShippingServiceAsScoped()
    {
        var services = new ServiceCollection();

        services.AddShippingModule(ConnectionString, new AppConfig(), isProduction: false);

        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(IShippingService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    // Chi tiết cấu hình không được lộ cho khách, nên thông báo chỉ nói cửa hàng chưa sẵn sàng.
    public void ShippingNotConfiguredException_HasStableCodeStatusAndCustomerMessage()
    {
        var exception = new ShippingNotConfiguredException();

        Assert.Equal("SHIPPING_NOT_CONFIGURED", ShippingNotConfiguredException.ErrorCode);
        Assert.Equal("SHIPPING_NOT_CONFIGURED", exception.Code);
        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("Cửa hàng chưa sẵn sàng giao hàng, vui lòng thử lại sau.", exception.Message);
    }

    [Fact]
    public void BuildParcel_DelegatesToParcelBuilder()
    {
        var provider = new ServiceCollection()
            .AddLogging()
            // Module identity đăng ký bộ ghi audit ở ứng dụng thật. Test chỉ cần một bản không làm gì để dựng được `ShippingSettingsStore`.
            .AddSingleton<IAuditLogWriter, NoOpAuditLogWriter>()
            // `ShippingService` cần bộ phát sự kiện, mà ứng dụng thật đăng ký ở `Pickory.Shared`.
            .AddSingleton<IEventDispatcher, InProcessEventDispatcher>()
            .AddShippingModule(ConnectionString, new AppConfig(), isProduction: false)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IShippingService>();

        var parcel = service.BuildParcel([new ParcelItem(new Parcel(500, 10, 8, 3), 2)]);

        Assert.Equal(new Parcel(1000, 10, 8, 6), parcel);
    }

    private sealed class NoOpAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
