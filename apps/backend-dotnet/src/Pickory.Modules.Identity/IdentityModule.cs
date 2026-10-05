using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Identity.Persistence;

namespace Pickory.Modules.Identity;

// Điểm đăng ký duy nhất của module Identity. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string databaseConnectionString)
    {
        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng enum và lịch sử. Ứng dụng không tự chạy migration khi khởi động.
        services.AddDbContext<IdentityDbContext>(options =>
            IdentityDbContext.ConfigureOptions(options, databaseConnectionString));
        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Module chưa có route ở bước này. Task port tính năng gắn route của module tại đây.
        return endpoints;
    }
}
