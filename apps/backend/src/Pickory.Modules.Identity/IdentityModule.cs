using Npgsql;
using Pickory.Shared.Persistence;
using Microsoft.AspNetCore.Http;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Identity.Actors;
using Pickory.Modules.Identity.Audit;
using Pickory.Modules.Identity.Consent;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Features.AdminAuditLogs;
using Pickory.Modules.Identity.Features.AdminCustomers;
using Pickory.Modules.Identity.Features.AdminRoles;
using Pickory.Modules.Identity.Features.AdminStaff;
using Pickory.Modules.Identity.Features.ChangePassword;
using Pickory.Modules.Identity.Features.DeleteAccount;
using Pickory.Modules.Identity.Features.ForgotPassword;
using Pickory.Modules.Identity.Features.GetMe;
using Pickory.Modules.Identity.Features.GoogleSignIn;
using Pickory.Modules.Identity.Features.Login;
using Pickory.Modules.Identity.Features.Logout;
using Pickory.Modules.Identity.Features.Register;
using Pickory.Modules.Identity.Features.ResetPassword;
using Pickory.Modules.Identity.Features.SaveConsent;
using Pickory.Modules.Identity.Features.UpdateProfile;
using Pickory.Modules.Identity.Features.VerifyEmail;
using Pickory.Modules.Identity.GoogleOAuth;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Setup;
using Pickory.Modules.Identity.Throttling;
using Pickory.Shared.Events;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity;

// Điểm đăng ký duy nhất của module Identity. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string databaseConnectionString)
    {
        // Enum của module vào data source chung. Mọi module chia nhau một pool kết nối, nên `DATABASE_POOL_MAX` áp cho cả tiến trình.
        services.AddSharedDataSource(databaseConnectionString, IdentityDbContext.MapEnums);
        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng enum và lịch sử. Ứng dụng không tự chạy migration khi khởi động.
        services.AddDbContext<IdentityDbContext>((provider, options) =>
            IdentityDbContext.ConfigureOptions(options, provider.GetRequiredService<NpgsqlDataSource>()));

        // Test thay đồng hồ hệ thống bằng đồng hồ giả. `TryAdd` giữ bản đã đăng ký trước.
        services.TryAddSingleton(TimeProvider.System);
        // Bộ đếm phải là singleton. Bộ đếm mới cho mỗi request không giới hạn được gì.
        services.AddSingleton<IdentityRateLimiters>();
        // Route nhận validator qua DI. Mỗi validator nằm cạnh request trong thư mục của tính năng.
        services.AddValidatorsFromAssembly(typeof(IdentityModule).Assembly, includeInternalTypes: true);
        // Handler dùng `DbContext` scoped. Mỗi request nhận một handler riêng.
        services.AddScoped<RegisterHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<VerifyEmailHandler>();
        services.AddScoped<ForgotPasswordHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<GoogleCallbackHandler>();
        services.AddScoped<UpdateProfileHandler>();
        services.AddScoped<ChangePasswordHandler>();
        services.AddScoped<DeleteAccountHandler>();
        services.AddScoped<RoleManagementHandler>();
        services.AddScoped<StaffManagementHandler>();
        services.AddScoped<CustomerManagementHandler>();
        services.AddScoped<AuditLogReadHandler>();
        // Module khác ghi nhật ký thao tác qua contract này. Writer không giữ trạng thái, nên dùng chung một instance.
        services.AddSingleton<IAuditLogWriter, TransactionalAuditLogWriter>();
        // Cảnh báo quyền lạ trong DB mỗi lần API khởi động, giống `server.ts` của bản Node.
        services.AddHostedService<StoredPermissionCheck>();
        // Gộp consent của mã ẩn danh vào tài khoản mỗi khi khách đăng ký hoặc đăng nhập.
        services.AddScoped<IDomainEventHandler<UserLinkedEvent>, ConsentLinkHandler>();
        // `HttpClient` do factory quản lý. Kết nối tới Google được dùng lại và không cạn socket.
        services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>();
        return services;
    }

    // Dựng hoặc cập nhật schema của module bằng migration EF Core.
    // API không tự chạy migration khi khởi động, nên chỉ lệnh chuẩn bị DB gọi hàm này.
    public static async Task MigrateIdentityDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(cancellationToken);
    }

    // Lệnh quản trị của identity, ví dụ `identity:seed`. Lệnh chạy xong thì thoát, không mở web API.
    public static bool IsIdentityCommand(IReadOnlyList<string> args)
    {
        return IdentityCommandLine.IsCommand(args);
    }

    public static Task<int> RunIdentityCommandAsync(IReadOnlyList<string> args, IServiceProvider services)
    {
        return IdentityCommandLine.RunAsync(args, services);
    }

    // Nạp sẵn tài khoản khách hàng phục vụ kiểm thử E2E.
    // Hàm chỉ được gọi từ tiến trình chuẩn bị DB E2E để tránh tạo tài khoản thử trong môi trường khác.
    public static async Task SeedE2eCustomersAsync(
        IServiceProvider services,
        IReadOnlyList<(string Email, string Name, string Phone)> customers,
        string password,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        foreach (var (email, name, phone) in customers)
        {
            // Tạo từng tài khoản khách kèm vai trò và mật khẩu đã băm.
            await CustomerAccountCreator.CreateAsync(database, email, password, name, phone, cancellationToken);
        }
    }

    public static IApplicationBuilder UseIdentityActor(this IApplicationBuilder app)
    {
        // Mọi route cần actor, trừ route webhook. Middleware này phải đứng trước mọi route khác.
        return app.UseWhen(
            // Hệ thống bên ngoài gọi webhook từ server, không có `Origin`, cookie hay actor. Webhook tự xác thực bằng secret riêng.
            context => !context.Request.Path.StartsWithSegments(WebhookRoutes.PathPrefix),
            branch => branch.UseMiddleware<ActorMiddleware>());
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Nhóm không thêm tiền tố. Nhóm chỉ gắn tag `Identity` cho mọi route của module trong tài liệu OpenAPI.
        var moduleRoutes = endpoints.MapGroup(string.Empty).WithTags("Identity");
        GetMeEndpoint.Map(moduleRoutes);
        RegisterEndpoint.Map(moduleRoutes);
        CustomerLoginEndpoint.Map(moduleRoutes);
        StaffLoginEndpoint.Map(moduleRoutes);
        LogoutEndpoint.Map(moduleRoutes);
        VerifyEmailEndpoint.Map(moduleRoutes);
        ForgotPasswordEndpoint.Map(moduleRoutes);
        ResetPasswordEndpoint.Map(moduleRoutes);
        GoogleStartEndpoint.Map(moduleRoutes);
        GoogleCallbackEndpoint.Map(moduleRoutes);
        SaveConsentEndpoint.Map(moduleRoutes);
        UpdateProfileEndpoint.Map(moduleRoutes);
        ChangePasswordEndpoint.Map(moduleRoutes);
        DeleteAccountEndpoint.Map(moduleRoutes);
        AdminRolesEndpoints.Map(moduleRoutes);
        AdminStaffEndpoints.Map(moduleRoutes);
        AdminCustomersEndpoints.Map(moduleRoutes);
        AdminAuditLogsEndpoints.Map(moduleRoutes);
        return endpoints;
    }
}
