using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Budget;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Usage;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Usage log là nguồn duy nhất của tổng chi phí tháng. Kiểm tra ngân sách đọc lại đúng các dòng đã ghi.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class UsageLogAndBudgetTests(IntegrationDatabaseFixture database)
{
    private UsageLogWriter CreateWriter()
    {
        return new UsageLogWriter(new FixtureAiGatewayContextFactory(database), TimeProvider.System, NullLogger<UsageLogWriter>.Instance);
    }

    private async Task ClearUsageLogsAsync()
    {
        await using var aiGateway = database.CreateAiGatewayContext();
        await aiGateway.UsageLogs.ExecuteDeleteAsync();
    }

    [Fact]
    // Dòng log có token, chi phí và hash của actor. Bảng không có cột chứa nội dung prompt.
    public async Task WriteAsync_StoresUsageCostAndHashedActor()
    {
        await ClearUsageLogsAsync();
        var entry = new UsageLogEntry(AiFeature.ShoppingAssistantChat, "deepseek", "deepseek-flash", UsageStatus.Ok)
        {
            PromptVersion = "  chat@1  ",
            Usage = new TokenUsage(1_000_000, 0, 0),
            LatencyMs = 12.6,
            ActorKey = "user:abc",
        };

        await CreateWriter().WriteAsync(entry, CancellationToken.None);

        await using var aiGateway = database.CreateAiGatewayContext();
        var row = await aiGateway.UsageLogs.AsNoTracking().SingleAsync();
        Assert.Equal(("shopping-assistant.chat", "deepseek", "deepseek-flash", "chat@1"), (row.Feature, row.Provider, row.Model, row.PromptVersion));
        Assert.Equal((1_000_000, 0, 0), (row.InputTokens, row.OutputTokens, row.CachedTokens));
        Assert.Equal(0.3, row.CostUsd, precision: 6);
        Assert.Equal(13, row.LatencyMs);
        Assert.Equal("ok", row.Status);
        Assert.Equal(UsageLogWriter.HashActorKey("user:abc"), row.ActorKeyHash);
        Assert.DoesNotContain("abc", row.ActorKeyHash);
    }

    [Fact]
    // Lời gọi bị chặn trước provider không có phí, kể cả khi người gọi truyền usage.
    public async Task WriteAsync_BlockedCallCostsNothing()
    {
        await ClearUsageLogsAsync();
        var entry = new UsageLogEntry(AiFeature.ShoppingAssistantChat, "deepseek", "deepseek-flash", UsageStatus.Blocked)
        {
            Usage = new TokenUsage(500, 10, 0),
            ErrorCode = "BUDGET_EXCEEDED",
        };

        await CreateWriter().WriteAsync(entry, CancellationToken.None);

        await using var aiGateway = database.CreateAiGatewayContext();
        var row = await aiGateway.UsageLogs.AsNoTracking().SingleAsync();
        Assert.Equal(("blocked", "BUDGET_EXCEEDED", 0d), (row.Status, row.ErrorCode, row.CostUsd));
    }

    [Fact]
    // Bước lỗi đã nhận token vẫn tính phí. Provider vẫn tính tiền cho token đã sinh.
    public async Task WriteAsync_ErrorWithTokensKeepsCost()
    {
        await ClearUsageLogsAsync();
        var entry = new UsageLogEntry(AiFeature.SmartSearchParseQuery, "deepseek", "deepseek-flash", UsageStatus.Error)
        {
            Usage = new TokenUsage(0, 1_000_000, 0),
            ErrorCode = "AI_INVALID_OUTPUT",
        };

        await CreateWriter().WriteAsync(entry, CancellationToken.None);

        await using var aiGateway = database.CreateAiGatewayContext();
        Assert.Equal(1.2, (await aiGateway.UsageLogs.AsNoTracking().SingleAsync()).CostUsd, precision: 6);
    }

    [Fact]
    // Tổng chi phí chỉ gồm các dòng của tháng hiện tại theo giờ Việt Nam.
    public async Task AssertWithinBudgetAsync_SumsOnlyCurrentVietnamMonth()
    {
        await ClearUsageLogsAsync();
        var writer = CreateWriter();
        var now = new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
        // 16:59 UTC ngày 30/9 vẫn là tháng 9 giờ Việt Nam. 17:00 UTC đã là ngày 1/10.
        await writer.WriteAsync(CostEntry(20, new DateTime(2026, 9, 30, 16, 59, 0, DateTimeKind.Utc)), CancellationToken.None);
        await writer.WriteAsync(CostEntry(5, new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
        await writer.WriteAsync(CostEntry(4, new DateTime(2026, 10, 15, 2, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
        var guard = new BudgetGuard(new FixtureAiGatewayContextFactory(database), new FixedTimeProvider(now), NullLogger<BudgetGuard>.Instance);

        var check = await guard.AssertWithinBudgetAsync(AiFeature.ShoppingAssistantChat, 30, CancellationToken.None);

        Assert.Equal(9, check.TotalCostUsd, precision: 6);
        Assert.False(check.BudgetWarning);
    }

    [Fact]
    // Chi phí tháng đạt 100% ngân sách. Tính năng ưu tiên thấp bị chặn ngay ở lần kiểm tra kế tiếp.
    public async Task AssertWithinBudgetAsync_BlocksLowPriorityAtFullBudget()
    {
        await ClearUsageLogsAsync();
        var now = new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
        await CreateWriter().WriteAsync(CostEntry(30, new DateTime(2026, 10, 15, 2, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
        var guard = new BudgetGuard(new FixtureAiGatewayContextFactory(database), new FixedTimeProvider(now), NullLogger<BudgetGuard>.Instance);

        await Assert.ThrowsAsync<AiBudgetExceededException>(
            () => guard.AssertWithinBudgetAsync(AiFeature.PersonalizedContentBanner, 30, CancellationToken.None));
    }

    // Dòng log có chi phí cố định. Model giả có giá 0, nên `CostUsd` truyền thẳng vào dòng log.
    private static UsageLogEntry CostEntry(double costUsd, DateTime createdAt)
    {
        return new UsageLogEntry(AiFeature.ShoppingAssistantChat, "fake", "fake-chat", UsageStatus.Ok)
        {
            CostUsd = costUsd,
            CreatedAt = createdAt,
        };
    }

    // Đồng hồ cố định. Test chọn tháng hiện tại mà không phụ thuộc ngày chạy test.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
