using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Pickory.Modules.AiGateway.Budget;
using Pickory.Modules.AiGateway.Contracts;
using Pickory.Modules.AiGateway.Embeddings;
using Pickory.Modules.AiGateway.Features.AdminSettings;
using Pickory.Modules.AiGateway.Features.AdminUsage;
using Pickory.Modules.AiGateway.Gateway;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.Providers.DeepSeek;
using Pickory.Modules.AiGateway.Providers.Fake;
using Pickory.Modules.AiGateway.Providers.OpenAi;
using Pickory.Modules.AiGateway.RateLimiting;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Settings;
using Pickory.Modules.AiGateway.Usage;
using Pickory.Shared.Configuration;

namespace Pickory.Modules.AiGateway;

// Điểm đăng ký duy nhất của module AiGateway. `Program.cs` không cần biết `DbContext` hoặc dịch vụ bên trong module.
public static class AiGatewayModule
{
    private const string DeepSeekHttpClient = "ai-gateway-deepseek";
    private const string OpenAiHttpClient = "ai-gateway-openai";

    public static IServiceCollection AddAiGatewayModule(this IServiceCollection services, string databaseConnectionString)
    {
        // Dùng cùng tùy chọn với migration để ứng dụng đọc đúng lịch sử migration.
        // Tùy chọn là singleton, nên gateway singleton tạo được context riêng cho mỗi thao tác qua factory.
        services.AddDbContext<AiGatewayDbContext>(
            options => AiGatewayDbContext.ConfigureOptions(options, databaseConnectionString),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<AiGatewayDbContext>(options =>
            AiGatewayDbContext.ConfigureOptions(options, databaseConnectionString));

        // Test thay đồng hồ hệ thống bằng đồng hồ giả. `TryAdd` giữ bản đã đăng ký trước.
        services.TryAddSingleton(TimeProvider.System);
        // Circuit breaker, hạn mức chat và lời gọi embedding đang chờ là trạng thái chung của cả API, nên dùng singleton.
        services.AddSingleton(provider => new ProviderReliability(provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ChatRateLimiter>();
        services.AddSingleton<EmbeddingCache>();
        services.AddSingleton<UsageLogWriter>();
        services.AddSingleton<BudgetGuard>();
        services.AddSingleton<AiSettingsStore>();
        services.AddSingleton<UsageSummaryReader>();

        // Gateway tự quản lý timeout từng lần gọi. HttpClient không đặt giới hạn riêng, nên stream dài không bị cắt.
        services.AddHttpClient(DeepSeekHttpClient, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddHttpClient(OpenAiHttpClient, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton(CreateProviders);
        services.AddSingleton<AiGatewayService>();
        // Module khác chỉ thấy `IAiGateway` trong project Contracts.
        services.AddSingleton<IAiGateway>(provider => provider.GetRequiredService<AiGatewayService>());
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
        AdminUsageEndpoint.Map(endpoints);
        AdminSettingsEndpoints.Map(endpoints);
        return endpoints;
    }

    // Chọn provider theo cấu hình. Production thiếu khóa thì không có provider, gateway ghi sự cố thay vì dùng provider giả.
    private static AiProviderSet CreateProviders(IServiceProvider provider)
    {
        var config = provider.GetRequiredService<AppConfig>();
        var isProduction = provider.GetRequiredService<IHostEnvironment>().IsProduction();
        var httpClients = provider.GetRequiredService<IHttpClientFactory>();

        IChatProvider? chat = null;
        if (config.LlmApiKey.Trim().Length > 0 && config.LlmModel.Length > 0)
        {
            chat = new DeepSeekChatProvider(config.LlmApiKey, config.LlmBaseUrl, config.LlmModel, httpClients.CreateClient(DeepSeekHttpClient));
        }
        else if (!isProduction)
        {
            chat = new FakeChatProvider();
        }

        IEmbeddingProvider? embedding = null;
        if (config.EmbeddingApiKey.Trim().Length > 0)
        {
            embedding = new OpenAiEmbeddingProvider(config.EmbeddingApiKey, config.EmbeddingModel, httpClients.CreateClient(OpenAiHttpClient));
        }
        else if (!isProduction)
        {
            embedding = new FakeEmbeddingProvider();
        }

        return new AiProviderSet(chat, embedding);
    }
}
