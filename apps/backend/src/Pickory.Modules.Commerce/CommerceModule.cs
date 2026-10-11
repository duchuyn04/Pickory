using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.Modules.Commerce.Carts;
using Pickory.Modules.Commerce.Configuration;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Commerce.Settings;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Shared.Events;
using Pickory.Shared.Configuration;

namespace Pickory.Modules.Commerce;

// Điểm đăng ký duy nhất của module Commerce. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class CommerceModule
{
    public static IServiceCollection AddCommerceModule(
        this IServiceCollection services, string databaseConnectionString, AppConfig config, bool isProduction)
    {
        // Production thiếu khóa SePay thì dừng ngay khi khởi động, trước khi nhận bất kỳ request nào.
        SepayConfigurationCheck.EnsureConfigured(config, isProduction);

        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng enum và lịch sử. Ứng dụng không tự chạy migration khi khởi động.
        services.AddDbContext<CommerceDbContext>(options =>
            CommerceDbContext.ConfigureOptions(options, databaseConnectionString));

        // Job hết hạn thanh toán và luật thời hạn cần đồng hồ. Module không giả định module khác đã đăng ký đồng hồ.
        services.TryAddSingleton(TimeProvider.System);

        // Giới hạn commerce đọc từ một lớp duy nhất. Giỏ hàng và đặt đơn cùng dùng nguồn giá trị này.
        services.AddScoped<CommerceSettingsProvider>();
        services.AddScoped<CartService>();
        services.AddScoped<CartMergeService>();

        // Gộp giỏ vãng lai khi khách đăng nhập, và xóa giỏ cùng sổ địa chỉ khi khách xóa tài khoản.
        services.AddScoped<IDomainEventHandler<UserLinkedEvent>, CartLinkHandler>();
        services.AddScoped<IDomainEventHandler<UserDeletedEvent>, UserDeletedCommerceHandler>();
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateCommerceDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CommerceDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public static IEndpointRouteBuilder MapCommerceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Nhóm không thêm tiền tố, và gắn tag `Commerce` cho mọi route của module trong tài liệu OpenAPI.
        var moduleRoutes = endpoints.MapGroup(string.Empty).WithTags("Commerce");
        CartEndpoints.Map(moduleRoutes);
        return endpoints;
    }
}
