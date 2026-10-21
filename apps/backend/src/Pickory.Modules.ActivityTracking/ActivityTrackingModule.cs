using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pickory.Modules.ActivityTracking.Counters;
using Pickory.Modules.ActivityTracking.Features.CollectEvents;
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

        // Bộ ghi đếm cần đồng hồ để chọn ngày giờ Việt Nam. Module không giả định module khác đã đăng ký đồng hồ.
        services.TryAddSingleton(TimeProvider.System);
        // Vòng đời scoped vì bộ ghi giữ `ActivityTrackingDbContext` của request.
        services.AddScoped<DailyCounterWriter>();
        // Bộ đếm tần suất nằm trong bộ nhớ, nên phải là singleton để mọi request dùng chung một bộ đếm.
        services.AddSingleton<ProductViewRateLimiter>();
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateActivityTrackingDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ActivityTrackingDbContext>().Database.MigrateAsync(cancellationToken);
    }

    // Hàm để `Program.cs` gọi một lần. Route phễu của ACT-11 sẽ thêm vào đây mà không phải sửa `Program.cs`.
    public static IEndpointRouteBuilder MapActivityTrackingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Nhóm không thêm tiền tố. Nhóm chỉ gắn tag `ActivityTracking` cho mọi route của module trong tài liệu OpenAPI.
        var moduleRoutes = endpoints.MapGroup(string.Empty).WithTags("ActivityTracking");
        CollectEventsEndpoints.Map(moduleRoutes);
        return endpoints;
    }
}
