using Pickory.Modules.AiGateway.Budget;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Settings;
using Pickory.Modules.AiGateway.Usage;

namespace Pickory.Modules.AiGateway.Gateway;

// Các dịch vụ mà mỗi bước gọi provider cần: ngân sách, độ tin cậy, đồng hồ và usage log.
public sealed class StepServices(
    ProviderReliability reliability,
    AiSettingsStore settingsStore,
    BudgetGuard budgetGuard,
    UsageLogWriter usageLogWriter,
    TimeProvider timeProvider)
{
    public const string UnavailableProvider = "unavailable";

    public ProviderReliability Reliability { get; } = reliability;
    public AiSettingsStore SettingsStore { get; } = settingsStore;
    public BudgetGuard Budget { get; } = budgetGuard;
    public TimeProvider Time { get; } = timeProvider;

    // Kiểm tra ngân sách trước mỗi bước. Admin vừa đổi ngân sách thì bước kế tiếp dùng ngay giá trị mới.
    public async Task AssertStepAllowedAsync(IAiProvider provider, CallContext context, CancellationToken cancellationToken)
    {
        var settings = await SettingsStore.GetAsync(cancellationToken);
        try
        {
            await Budget.AssertWithinBudgetAsync(context.Feature, settings.MonthlyBudgetUsd, cancellationToken);
        }
        catch (AiBudgetExceededException)
        {
            // Bước bị chặn vẫn có một dòng log. Dashboard thấy được thời gian tính năng bị dừng.
            await LogAsync(provider, context, UsageStatus.Blocked, "BUDGET_EXCEEDED", costUsd: 0);
            throw;
        }
    }

    // Log luôn được ghi, kể cả khi người gọi đã hủy request. Bước đã gọi provider vẫn có thể phát sinh phí.
    public Task LogAsync(
        IAiProvider? provider,
        CallContext context,
        UsageStatus status,
        string? errorCode = null,
        TokenUsage? usage = null,
        double latencyMs = 0,
        double? costUsd = null)
    {
        var entry = new UsageLogEntry(
            context.Feature,
            provider?.Name ?? UnavailableProvider,
            provider?.Model ?? UnavailableProvider,
            status)
        {
            PromptVersion = context.PromptVersion,
            Usage = usage,
            CostUsd = costUsd,
            LatencyMs = latencyMs,
            ErrorCode = errorCode,
            ActorKey = context.ActorKey,
        };
        return usageLogWriter.WriteAsync(entry, CancellationToken.None);
    }

    public double ElapsedMilliseconds(long startTimestamp)
    {
        return Time.GetElapsedTime(startTimestamp).TotalMilliseconds;
    }
}
