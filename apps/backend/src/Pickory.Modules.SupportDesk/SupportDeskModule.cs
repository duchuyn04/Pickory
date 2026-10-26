using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Shared.Events;
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

        // Nhịp tim và job `away` cần đồng hồ. Module không giả định module khác đã đăng ký đồng hồ.
        services.TryAddSingleton(TimeProvider.System);
        // Validator của module đều là `internal`, nên phải bật `includeInternalTypes` thì container mới thấy chúng.
        services.AddValidatorsFromAssemblyContaining<SetPresenceRequestValidator>(includeInternalTypes: true);
        // Hub là singleton vì mọi request SSE và mọi nơi phát sự kiện phải dùng chung một danh sách kết nối trong bộ nhớ.
        services.AddSingleton<SupportEventHub>();
        // Streamer không giữ trạng thái riêng nên là singleton. Nó tự tạo scope khi cần ghi DB.
        services.AddSingleton<SupportEventStreamer>();
        // Vòng đời scoped vì dịch vụ và job giữ `SupportDeskDbContext` của request hoặc của một vòng worker.
        services.AddScoped<SupportConversationService>();
        services.AddScoped<StaffPresenceService>();
        services.AddScoped<StaffPresenceTimeoutProcessor>();
        // Giới hạn tin là singleton vì bộ đếm trong bộ nhớ phải dùng chung cho mọi request của một khách.
        services.AddSingleton<SupportMessageLimiters>();
        // Đăng nhập phát `UserLinkedEvent`. Handler gắn hội thoại vãng lai vào tài khoản vừa đăng nhập.
        services.AddScoped<IDomainEventHandler<UserLinkedEvent>, SupportConversationLinkHandler>();
        // Worker chạy nền gọi processor mỗi 30 giây.
        services.AddHostedService<StaffPresenceTimeoutWorker>();
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateSupportDeskDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SupportDeskDbContext>().Database.MigrateAsync(cancellationToken);
    }

    // Hàm có sẵn để `Program.cs` gọi một lần, nên các task sau thêm route vào đây mà không phải sửa `Program.cs`.
    public static IEndpointRouteBuilder MapSupportDeskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Nhóm không thêm tiền tố. Nhóm chỉ gắn tag `SupportDesk` cho mọi route của module trong tài liệu OpenAPI.
        var moduleRoutes = endpoints.MapGroup(string.Empty).WithTags("SupportDesk");
        SupportEventsEndpoints.Map(moduleRoutes);
        StaffPresenceEndpoints.Map(moduleRoutes);
        SupportConversationEndpoints.Map(moduleRoutes);
        return endpoints;
    }
}
