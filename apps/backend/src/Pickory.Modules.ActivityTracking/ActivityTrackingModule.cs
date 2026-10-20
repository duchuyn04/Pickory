using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.Modules.ActivityTracking.Persistence;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.ActivityTracking;

// Điểm đăng ký duy nhất của module ActivityTracking. `Program.cs` không cần biết `DbContext` bên trong module.
public static class ActivityTrackingModule
{
    public static IServiceCollection AddActivityTrackingModule(
        this IServiceCollection services, string databaseConnectionString)
    {
        // Mọi module chia nhau một data source, nên `DATABASE_POOL_MAX` áp cho cả tiến trình.
        // Module không có enum Postgres, nên hàm cấu hình để trống.
        services.AddSharedDataSource(databaseConnectionString, _ => { });

        // Ứng dụng không tự chạy migration khi khởi động. Chỉ lệnh chuẩn bị DB gọi `MigrateActivityTrackingDatabaseAsync`.
        services.AddDbContext<ActivityTrackingDbContext>((provider, options) =>
            ActivityTrackingDbContext.ConfigureOptions(options, provider.GetRequiredService<NpgsqlDataSource>()));
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateActivityTrackingDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ActivityTrackingDbContext>().Database.MigrateAsync(cancellationToken);
    }

    // Chưa gắn route nào, vì ACT-06 và ACT-11 mới thêm route đếm lượt và route phễu.
    // Hàm có sẵn để `Program.cs` gọi một lần, nên hai task sau không phải sửa `Program.cs`.
    public static IEndpointRouteBuilder MapActivityTrackingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
