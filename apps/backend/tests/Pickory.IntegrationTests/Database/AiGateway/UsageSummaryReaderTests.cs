using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Entities;
using Pickory.Modules.AiGateway.Settings;
using Pickory.Modules.AiGateway.Usage;
using Pickory.Modules.Identity.Audit;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Port phần `getUsageSummary` của `usage-summary.test.ts` ở bản Node, chạy trên DB thật.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class UsageSummaryReaderTests(IntegrationDatabaseFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

    private async Task<UsageSummaryReader> CreateReaderAsync(double monthlyBudgetUsd, params AiUsageLog[] logs)
    {
        await using (var aiGateway = database.CreateAiGatewayContext())
        {
            await aiGateway.UsageLogs.ExecuteDeleteAsync();
            await aiGateway.Settings.ExecuteDeleteAsync();
            aiGateway.Settings.Add(new AiSetting
            {
                Id = AiSettingsStore.SingletonId,
                MonthlyBudgetUsd = monthlyBudgetUsd,
                ChatDailyLimitUser = 50,
                ChatDailyLimitAnon = 15,
                UpdatedAt = DateTime.UtcNow,
            });
            aiGateway.UsageLogs.AddRange(logs);
            await aiGateway.SaveChangesAsync();
        }

        var factory = new FixtureAiGatewayContextFactory(database);
        var time = new FakeTimeProvider(Now);
        var settings = new AiSettingsStore(factory, new AppConfig { DatabaseUrl = database.DatabaseUrl }, new TransactionalAuditLogWriter(), time);
        return new UsageSummaryReader(factory, settings, time);
    }

    private static AiUsageLog Log(double costUsd, int latencyMs, string status, string feature, DateTime createdAt)
    {
        return new AiUsageLog
        {
            Feature = feature,
            Provider = "fake",
            Model = "fake-chat",
            CostUsd = costUsd,
            LatencyMs = latencyMs,
            Status = status,
            CreatedAt = createdAt,
        };
    }

    private static DateTime Utc(int month, int day, int hour, int minute = 0, int second = 0)
    {
        return new DateTime(2026, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    [Fact]
    // Ngày tính theo giờ Việt Nam. 16:59:59 UTC ngày 4 thuộc ngày 4, còn 17:00 UTC đã thuộc ngày 5.
    public async Task ReadAsync_GroupsByVietnamDay()
    {
        var reader = await CreateReaderAsync(30,
            Log(0.05, 120, "ok", "shopping-assistant.chat", Utc(10, 4, 16, 59, 59)),
            Log(0.1, 250, "ok", "shopping-assistant.chat", Utc(10, 4, 17)),
            Log(0.15, 300, "ok", "smart-search.parse-query", Utc(10, 5, 5)));

        var summary = await reader.ReadAsync(Utc(10, 4, 0), Utc(10, 5, 23, 59, 59), CancellationToken.None);

        Assert.Equal(2, summary.ByDay.Count);
        Assert.Equal(("2026-10-04", 1), (summary.ByDay[0].Day, summary.ByDay[0].Calls));
        Assert.Equal(0.05, summary.ByDay[0].CostUsd, precision: 10);
        Assert.Equal(("2026-10-05", 2), (summary.ByDay[1].Day, summary.ByDay[1].Calls));
        Assert.Equal(0.25, summary.ByDay[1].CostUsd, precision: 10);
    }

    [Fact]
    // Dòng ngoài khoảng thời gian bị loại. Hai mốc đầu và cuối thuộc khoảng.
    public async Task ReadAsync_ExcludesLogsOutsideRange()
    {
        var reader = await CreateReaderAsync(30,
            Log(0.08, 180, "ok", "shopping-assistant.chat", Utc(10, 1, 0)),
            Log(0.02, 150, "ok", "shopping-assistant.chat", Utc(10, 5, 10)),
            Log(0.03, 150, "ok", "shopping-assistant.chat", Utc(10, 5, 23, 59, 59)),
            Log(0.09, 200, "ok", "shopping-assistant.chat", Utc(10, 10, 0)));

        var summary = await reader.ReadAsync(Utc(10, 5, 0), Utc(10, 5, 23, 59, 59), CancellationToken.None);

        Assert.Equal(2, summary.Calls);
        Assert.Equal(0.05, summary.CostUsd, precision: 10);
    }

    [Fact]
    // Chi phí rất nhỏ của embedding không bị làm tròn thành 0.
    public async Task ReadAsync_KeepsTinyCosts()
    {
        var reader = await CreateReaderAsync(30, Log(0.00000002, 40, "ok", "smart-search.index", Utc(10, 5, 10)));

        var summary = await reader.ReadAsync(Utc(10, 5, 0), Utc(10, 5, 23), CancellationToken.None);

        Assert.Equal(0.00000002, summary.CostUsd);
        Assert.Equal(0.00000002, Assert.Single(summary.ByFeature).CostUsd);
    }

    [Fact]
    // Tỉ lệ lỗi chỉ đếm `error`. Bước bị chặn không phải lỗi của provider.
    public async Task ReadAsync_ErrorRateCountsOnlyErrors()
    {
        var reader = await CreateReaderAsync(30,
            Log(0, 100, "ok", "shopping-assistant.chat", Utc(10, 5, 1)),
            Log(0, 200, "error", "shopping-assistant.chat", Utc(10, 5, 2)),
            Log(0, 300, "blocked", "shopping-assistant.chat", Utc(10, 5, 3)),
            Log(0, 400, "ok", "smart-search.index", Utc(10, 5, 4)));

        var summary = await reader.ReadAsync(Utc(10, 5, 0), Utc(10, 5, 23), CancellationToken.None);

        Assert.Equal(0.25, summary.ErrorRate);
        Assert.Equal(400, summary.P95LatencyMs);
    }

    [Fact]
    // Nhóm theo tính năng xếp theo chi phí giảm dần. Chi phí bằng nhau thì xếp theo tên.
    public async Task ReadAsync_SortsFeaturesByCostThenName()
    {
        var reader = await CreateReaderAsync(30,
            Log(0.1, 100, "ok", "smart-search.index", Utc(10, 5, 1)),
            Log(0.3, 100, "ok", "shopping-assistant.chat", Utc(10, 5, 2)),
            Log(0.1, 100, "ok", "knowledge-base.query", Utc(10, 5, 3)));

        var summary = await reader.ReadAsync(Utc(10, 5, 0), Utc(10, 5, 23), CancellationToken.None);

        Assert.Equal(["shopping-assistant.chat", "knowledge-base.query", "smart-search.index"], summary.ByFeature.Select(group => group.Feature));
    }

    [Fact]
    // Cờ cảnh báo so tổng chi phí của cả tháng hiện tại với 80% ngân sách, không chỉ chi phí trong khoảng báo cáo.
    public async Task ReadAsync_BudgetWarningUsesWholeCurrentMonth()
    {
        var reader = await CreateReaderAsync(10,
            Log(8, 100, "ok", "shopping-assistant.chat", Utc(10, 2, 1)),
            Log(0.01, 100, "ok", "shopping-assistant.chat", Utc(10, 14, 1)));

        var summary = await reader.ReadAsync(Utc(10, 14, 0), Utc(10, 14, 23), CancellationToken.None);

        Assert.Equal(1, summary.Calls);
        Assert.True(summary.BudgetWarning);
    }
}
