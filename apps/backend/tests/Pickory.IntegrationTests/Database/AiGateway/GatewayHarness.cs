using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Budget;
using Pickory.Modules.AiGateway.Embeddings;
using Pickory.Modules.AiGateway.Entities;
using Pickory.Modules.AiGateway.Gateway;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.Providers.Fake;
using Pickory.Modules.AiGateway.RateLimiting;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Settings;
using Pickory.Modules.AiGateway.Usage;
using Pickory.Modules.Identity.Audit;
using Pickory.Shared.Configuration;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Dựng gateway với provider giả và DB test. Mỗi test bắt đầu với bảng AI trống và cấu hình do test chọn.
public sealed class GatewayHarness
{
    private readonly IntegrationDatabaseFixture _database;

    private GatewayHarness(
        IntegrationDatabaseFixture database,
        IChatProvider? chatProvider,
        IEmbeddingProvider? embeddingProvider,
        ProviderReliability reliability,
        TimeProvider timeProvider,
        IDbContextFactory<AiGatewayDbContext>? embeddingCacheFactory)
    {
        _database = database;
        ChatProvider = chatProvider;
        EmbeddingProvider = embeddingProvider;
        Reliability = reliability;
        var databaseFactory = new FixtureAiGatewayContextFactory(database);
        var config = new AppConfig { DatabaseUrl = database.DatabaseUrl };
        var usageLogWriter = new UsageLogWriter(databaseFactory, timeProvider, NullLogger<UsageLogWriter>.Instance);
        Gateway = new AiGatewayService(
            new AiProviderSet(chatProvider, embeddingProvider),
            reliability,
            new ChatRateLimiter(timeProvider),
            new AiSettingsStore(databaseFactory, config, new TransactionalAuditLogWriter(), timeProvider),
            new BudgetGuard(databaseFactory, timeProvider, NullLogger<BudgetGuard>.Instance),
            usageLogWriter,
            new EmbeddingCache(embeddingCacheFactory ?? databaseFactory),
            timeProvider);
    }

    public AiGatewayService Gateway { get; }
    public IChatProvider? ChatProvider { get; }
    public IEmbeddingProvider? EmbeddingProvider { get; }
    public ProviderReliability Reliability { get; }

    public FakeChatProvider Chat => ChatProvider as FakeChatProvider ?? throw new InvalidOperationException("Harness không có provider chat giả.");

    public FakeEmbeddingProvider Embedding => EmbeddingProvider as FakeEmbeddingProvider ?? throw new InvalidOperationException("Harness không có provider embedding giả.");

    // Factory có interceptor. Test làm lệnh đọc hoặc ghi cache thất bại một lần.
    public static IDbContextFactory<AiGatewayDbContext> CreateFailingFactory(
        IntegrationDatabaseFixture database,
        FailOnceCommandInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>();
        AiGatewayDbContext.ConfigureOptions(options, database.ConnectionString);
        options.AddInterceptors(interceptor);
        return new OptionsDbContextFactory(options.Options);
    }

    public async Task<int> CountCacheRowsAsync()
    {
        await using var aiGateway = _database.CreateAiGatewayContext();
        return await aiGateway.EmbeddingCache.CountAsync();
    }

    public async Task AddCacheRowAsync(string key, double[] embedding)
    {
        await using var aiGateway = _database.CreateAiGatewayContext();
        aiGateway.EmbeddingCache.Add(new AiEmbeddingCache { Key = key, Model = "fake-embedding", Embedding = embedding });
        await aiGateway.SaveChangesAsync();
    }

    private sealed class OptionsDbContextFactory(DbContextOptions<AiGatewayDbContext> options)
        : IDbContextFactory<AiGatewayDbContext>
    {
        public AiGatewayDbContext CreateDbContext()
        {
            return new AiGatewayDbContext(options);
        }
    }

    // Mặc định có cả hai provider giả. Test truyền null để giả lập môi trường thiếu khóa provider.
    public static async Task<GatewayHarness> CreateAsync(
        IntegrationDatabaseFixture database,
        AiSettings? settings = null,
        IChatProvider? chatProvider = null,
        IEmbeddingProvider? embeddingProvider = null,
        bool withoutProviders = false,
        ProviderReliability? reliability = null,
        TimeProvider? timeProvider = null,
        IDbContextFactory<AiGatewayDbContext>? embeddingCacheFactory = null)
    {
        var time = timeProvider ?? TimeProvider.System;
        var harness = new GatewayHarness(
            database,
            withoutProviders ? null : chatProvider ?? new FakeChatProvider(),
            withoutProviders ? null : embeddingProvider ?? new FakeEmbeddingProvider(),
            reliability ?? new ProviderReliability(time, () => 0),
            time,
            embeddingCacheFactory);
        await harness.ResetTablesAsync();
        await harness.SetSettingsAsync(settings ?? new AiSettings(30, 50, 15));
        return harness;
    }

    // Ghi thẳng dòng cấu hình. Test đổi hạn mức giữa hai lời gọi để kiểm tra gateway đọc lại cấu hình.
    public async Task SetSettingsAsync(AiSettings settings)
    {
        await using var aiGateway = _database.CreateAiGatewayContext();
        await aiGateway.Settings.ExecuteDeleteAsync();
        aiGateway.Settings.Add(new AiSetting
        {
            Id = AiSettingsStore.SingletonId,
            MonthlyBudgetUsd = settings.MonthlyBudgetUsd,
            ChatDailyLimitUser = settings.ChatDailyLimitUser,
            ChatDailyLimitAnon = settings.ChatDailyLimitAnon,
            UpdatedAt = DateTime.UtcNow,
        });
        await aiGateway.SaveChangesAsync();
    }

    public async Task<List<AiUsageLog>> ReadLogsAsync()
    {
        await using var aiGateway = _database.CreateAiGatewayContext();
        return await aiGateway.UsageLogs.AsNoTracking().OrderBy(log => log.CreatedAt).ToListAsync();
    }

    public async Task AddCostAsync(double costUsd)
    {
        await using var aiGateway = _database.CreateAiGatewayContext();
        aiGateway.UsageLogs.Add(new AiUsageLog
        {
            Feature = "smart-search.parse-query",
            Provider = "fake",
            Model = "fake-chat",
            CostUsd = costUsd,
            Status = "ok",
            CreatedAt = DateTime.UtcNow.AddMilliseconds(-1),
        });
        await aiGateway.SaveChangesAsync();
    }

    private async Task ResetTablesAsync()
    {
        await using var aiGateway = _database.CreateAiGatewayContext();
        await aiGateway.UsageLogs.ExecuteDeleteAsync();
        await aiGateway.EmbeddingCache.ExecuteDeleteAsync();
    }
}
