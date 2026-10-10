using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.Modules.Shipping.Addresses;
using Pickory.Modules.Shipping.Configuration;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Features.AdminSettings;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Modules.Shipping.Shipments;
using Pickory.Modules.Shipping.Settings;
using Pickory.Modules.Shipping.Sync;
using Pickory.Modules.Shipping.Webhooks;
using Pickory.Shared.Configuration;

namespace Pickory.Modules.Shipping;

// Điểm đăng ký duy nhất của module Shipping. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class ShippingModule
{
    public static IServiceCollection AddShippingModule(
        this IServiceCollection services, string databaseConnectionString, AppConfig config, bool isProduction)
    {
        // Production thiếu biến GHN thì dừng ngay khi khởi động, trước khi nhận bất kỳ request nào.
        GhnConfigurationCheck.EnsureConfigured(config, isProduction);

        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng enum và lịch sử. Ứng dụng không tự chạy migration khi khởi động.
        services.AddDbContext<ShippingDbContext>(options =>
            ShippingDbContext.ConfigureOptions(options, databaseConnectionString));

        // Client GHN thật khi có token, client giả khi thiếu. Module khác không thấy `IGhnClient` vì kiểu này là `internal`.
        services.AddGhnClient(config);

        // Bộ đồng bộ địa chỉ cần đồng hồ để ghi `syncedAt` và đặt chu kỳ. Module không giả định module khác đã đăng ký đồng hồ.
        services.TryAddSingleton(TimeProvider.System);
        // Worker nền đồng bộ danh mục địa chỉ mỗi 24 giờ. Lệnh `shipping:sync-addresses` dùng cùng bộ đồng bộ.
        services.AddScoped<IAddressSyncProcessor, AddressSyncProcessor>();
        services.AddHostedService<AddressSyncWorker>();

        // Job tra lại trạng thái vận đơn mỗi 30 phút để bù webhook bị mất. Webhook và job dùng chung `ShipmentStatusUpdater`.
        services.AddScoped<IShipmentStatusSyncProcessor, ShipmentStatusSyncProcessor>();
        services.AddHostedService<ShipmentStatusSyncWorker>();

        // Bộ đếm tần suất của webhook nằm trong bộ nhớ, nên phải là singleton để mọi request dùng chung một bộ đếm.
        services.AddSingleton<GhnWebhookRateLimiter>();

        // Lớp áp trạng thái GHN dùng chung cho webhook và job đồng bộ. Vòng đời scoped vì lớp giữ `ShippingDbContext` của request.
        services.AddScoped<ShipmentStatusUpdater>();

        // Hai lớp cấu hình dùng `ShippingDbContext` theo request, nên đăng ký scoped.
        services.AddScoped<ShippingSettingsValidator>();
        services.AddScoped<ShippingSettingsStore>();

        // Cổng công khai cho module `commerce`. Vòng đời scoped vì dịch vụ giữ `ShippingDbContext` của request.
        services.AddScoped<IShippingService, ShippingService>();
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateShippingDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShippingDbContext>().Database.MigrateAsync(cancellationToken);
    }

    // Đồng bộ danh mục địa chỉ một lần và chờ xong, vì `e2e:start` chỉ gọi được bộ đồng bộ `internal` qua hàm này.
    // Đồng bộ thất bại thì ném lỗi, để bên gọi dừng thay vì mở API với danh mục rỗng.
    public static async Task SyncAddressesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IAddressSyncProcessor>();
        var result = await processor.SyncAsync(cancellationToken);

        // Kết quả thất bại gồm GHN lỗi và danh sách tỉnh/thành rỗng. Cả hai đều không được bỏ qua âm thầm.
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Đồng bộ danh mục địa chỉ thất bại: {result.FailureReason}");
        }
    }

    // Lệnh `shipping:sync-addresses` đồng bộ danh mục địa chỉ một lần. Lệnh do `Program.cs` gọi vì chạy ngoài web API.
    public static bool IsShippingCommand(IReadOnlyList<string> args) => ShippingCommandLine.IsSyncAddressesCommand(args);

    public static Task<int> RunShippingCommandAsync(IServiceProvider services) =>
        ShippingCommandLine.RunSyncAddressesAsync(services, Console.Out, Console.Error, CancellationToken.None);

    public static IEndpointRouteBuilder MapShippingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Nhóm không thêm tiền tố. Nhóm chỉ gắn tag `Shipping` cho mọi route của module trong tài liệu OpenAPI.
        var moduleRoutes = endpoints.MapGroup(string.Empty).WithTags("Shipping");
        AddressEndpoints.Map(moduleRoutes);
        ShippingSettingsEndpoints.Map(moduleRoutes);
        GhnWebhookEndpoint.Map(moduleRoutes);
        return endpoints;
    }
}
