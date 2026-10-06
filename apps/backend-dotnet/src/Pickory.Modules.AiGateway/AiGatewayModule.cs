using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.AiGateway.Persistence;

namespace Pickory.Modules.AiGateway;

// Điểm đăng ký duy nhất của module AiGateway. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class AiGatewayModule
{
    public static IServiceCollection AddAiGatewayModule(this IServiceCollection services, string databaseConnectionString)
    {
        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng enum và lịch sử. Ứng dụng không tự chạy migration khi khởi động.
        services.AddDbContext<AiGatewayDbContext>(options =>
            AiGatewayDbContext.ConfigureOptions(options, databaseConnectionString));
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateAiGatewayDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AiGatewayDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public static IEndpointRouteBuilder MapAiGatewayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Module chưa có route ở bước này. Task port tính năng gắn route của module tại đây.
        return endpoints;
    }
}
