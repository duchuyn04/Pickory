using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Identity.Actors;
using Pickory.Modules.Identity.Consent;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Emails;
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
using Pickory.Modules.Identity.Throttling;
using Pickory.Shared.Events;

namespace Pickory.Modules.Identity;

// Điểm đăng ký duy nhất của module Identity. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string databaseConnectionString)
    {
        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng enum và lịch sử. Ứng dụng không tự chạy migration khi khởi động.
        services.AddDbContext<IdentityDbContext>(options =>
            IdentityDbContext.ConfigureOptions(options, databaseConnectionString));

        // Test thay đồng hồ hệ thống bằng đồng hồ giả. `TryAdd` giữ bản đã đăng ký trước.
        services.TryAddSingleton(TimeProvider.System);
        // Bộ đếm phải là singleton. Bộ đếm mới cho mỗi request không giới hạn được gì.
        services.AddSingleton<IdentityRateLimiters>();
        // Dự án chưa nối dịch vụ gửi thư thật. Thư được ghi ra file để người dev mở link.
        services.AddSingleton<IEmailSender>(CreateDevOutboxEmailSender);
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
        // Gộp consent của mã ẩn danh vào tài khoản mỗi khi khách đăng ký hoặc đăng nhập.
        services.AddScoped<IDomainEventHandler<UserLinkedEvent>, ConsentLinkHandler>();
        // `HttpClient` do factory quản lý. Kết nối tới Google được dùng lại và không cạn socket.
        services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>();
        return services;
    }

    public static IApplicationBuilder UseIdentityActor(this IApplicationBuilder app)
    {
        // Mọi route cần actor, kể cả route của module khác. Middleware này phải đứng trước mọi route.
        return app.UseMiddleware<ActorMiddleware>();
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        GetMeEndpoint.Map(endpoints);
        RegisterEndpoint.Map(endpoints);
        CustomerLoginEndpoint.Map(endpoints);
        StaffLoginEndpoint.Map(endpoints);
        LogoutEndpoint.Map(endpoints);
        VerifyEmailEndpoint.Map(endpoints);
        ForgotPasswordEndpoint.Map(endpoints);
        ResetPasswordEndpoint.Map(endpoints);
        GoogleStartEndpoint.Map(endpoints);
        GoogleCallbackEndpoint.Map(endpoints);
        SaveConsentEndpoint.Map(endpoints);
        UpdateProfileEndpoint.Map(endpoints);
        ChangePasswordEndpoint.Map(endpoints);
        DeleteAccountEndpoint.Map(endpoints);
        return endpoints;
    }

    // `DEV_OUTBOX_DIR` cho phép E2E tách thư riêng. Mặc định là `.dev-outbox` trong thư mục chạy API, giống bản Node.
    private static DevOutboxEmailSender CreateDevOutboxEmailSender(IServiceProvider provider)
    {
        var configuredDirectory = provider.GetRequiredService<IConfiguration>()["DEV_OUTBOX_DIR"];
        var outboxDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(Directory.GetCurrentDirectory(), ".dev-outbox")
            : configuredDirectory;
        return new DevOutboxEmailSender(
            outboxDirectory,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<DevOutboxEmailSender>>());
    }
}
