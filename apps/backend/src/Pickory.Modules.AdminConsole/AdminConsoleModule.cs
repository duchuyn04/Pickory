using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.Modules.AdminConsole.Reports;

namespace Pickory.Modules.AdminConsole;

// Điểm đăng ký duy nhất của module AdminConsole. `Program.cs` không cần biết chi tiết bên trong module.
public static class AdminConsoleModule
{
    // Đăng ký dịch vụ của module AdminConsole vào DI container.
    public static IServiceCollection AddAdminConsoleModule(this IServiceCollection services)
    {
        // Đồng hồ hệ thống cần thiết để đọc ngày hôm nay theo giờ Việt Nam.
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    // Gắn các route của module AdminConsole vào ứng dụng.
    public static IEndpointRouteBuilder MapAdminConsoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Gắn nhóm route với nhãn AdminConsole để hiển thị đúng trong tài liệu OpenAPI.
        var group = endpoints.MapGroup(string.Empty).WithTags("AdminConsole");
        // Gắn các điểm cuối của báo cáo doanh thu vào nhóm route.
        SalesReportEndpoints.Map(group);
        return endpoints;
    }
}
