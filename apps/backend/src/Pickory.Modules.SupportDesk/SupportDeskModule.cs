using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.SupportDesk;

// Điểm đăng ký duy nhất của module SupportDesk. `Program.cs` không cần biết `DbContext` bên trong module.
public static class SupportDeskModule
{
    public static IServiceCollection AddSupportDeskModule(
        this IServiceCollection services, string databaseConnectionString)
    {
        // Mọi module chia nhau một data source, nên `DATABASE_POOL_MAX` áp cho cả tiến trình.
        // Data source phải biết bốn enum Postgres của module, nếu không Npgsql không đọc và ghi được cột enum.
        services.AddSharedDataSource(databaseConnectionString, SupportDeskDbContext.MapEnums);

        // Ứng dụng không tự chạy migration khi khởi động. Chỉ lệnh chuẩn bị DB gọi `MigrateSupportDeskDatabaseAsync`.
        services.AddDbContext<SupportDeskDbContext>((provider, options) =>
            SupportDeskDbContext.ConfigureOptions(options, provider.GetRequiredService<NpgsqlDataSource>()));
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateSupportDeskDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>().Database.MigrateAsync(cancellationToken);
    }

    // Nhóm route đã có tag `SupportDesk` nhưng chưa có route nào, vì các task sau mới thêm route.
    // Hàm có sẵn để `Program.cs` gọi một lần, nên các task sau không phải sửa `Program.cs`.
    public static IEndpointRouteBuilder MapSupportDeskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup(string.Empty).WithTags("SupportDesk");
        return endpoints;
    }
}
