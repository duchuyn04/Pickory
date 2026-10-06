using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Settings;
using Pickory.Modules.Identity.Audit;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Port từ `settings.int.test.ts` của bản Node. Cấu hình AI là một dòng duy nhất trong `ai_settings`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AiSettingsStoreTests(IntegrationDatabaseFixture database)
{
    private static readonly AppConfig Config = new()
    {
        DatabaseUrl = "postgresql://unused",
        AiMonthlyBudgetUsd = 30,
        AiChatDailyLimitUser = 50,
        AiChatDailyLimitAnon = 15,
    };

    private AiSettingsStore CreateStore(IAuditLogWriter? auditLogWriter = null)
    {
        return new AiSettingsStore(
            new FixtureAiGatewayContextFactory(database),
            Config,
            auditLogWriter ?? new TransactionalAuditLogWriter(),
            TimeProvider.System);
    }

    private async Task DeleteSettingsRowAsync()
    {
        await using var aiGateway = database.CreateAiGatewayContext();
        await aiGateway.Settings.ExecuteDeleteAsync();
    }

    private async Task<int> CountSettingsAuditRowsAsync(string actorId)
    {
        await using var identity = database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync(log => log.Action == "ai_settings.update" && log.ActorId == actorId);
    }

    [Fact]
    // DB chưa có dòng cấu hình. Lần đọc đầu tạo dòng từ giá trị mặc định của biến môi trường.
    public async Task GetAsync_CreatesDefaultRowWhenMissing()
    {
        await DeleteSettingsRowAsync();

        var settings = await CreateStore().GetAsync(CancellationToken.None);

        Assert.Equal(new AiSettings(30, 50, 15), settings);
        await using var aiGateway = database.CreateAiGatewayContext();
        var row = await aiGateway.Settings.AsNoTracking().SingleAsync();
        Assert.Equal(AiSettingsStore.SingletonId, row.Id);
        Assert.Equal(30, row.MonthlyBudgetUsd);
    }

    [Fact]
    // Sửa cấu hình lưu giá trị mới và tạo đúng 1 dòng audit có trạng thái sau.
    public async Task UpdateAsync_SavesSettingsAndWritesOneAuditRow()
    {
        await DeleteSettingsRowAsync();
        var actorId = $"test-admin-{Guid.NewGuid():N}";

        var updated = await CreateStore().UpdateAsync(new AiSettings(150, 80, 20), actorId, CancellationToken.None);

        Assert.Equal(new AiSettings(150, 80, 20), updated);
        await using var aiGateway = database.CreateAiGatewayContext();
        var row = await aiGateway.Settings.AsNoTracking().SingleAsync();
        Assert.Equal((150d, 80, 20), (row.MonthlyBudgetUsd, row.ChatDailyLimitUser, row.ChatDailyLimitAnon));
        await using var identity = database.CreateIdentityContext();
        var audit = await identity.AuditLogs.AsNoTracking().SingleAsync(log => log.ActorId == actorId);
        Assert.Equal(("ai_settings.update", "ai_setting", AiSettingsStore.SingletonId), (audit.Action, audit.TargetType, audit.TargetId));
        Assert.Equal(150, audit.After!.RootElement.GetProperty("monthlyBudgetUsd").GetDouble());
        Assert.Equal(80, audit.After.RootElement.GetProperty("chatDailyLimitUser").GetInt32());
        Assert.Equal(20, audit.After.RootElement.GetProperty("chatDailyLimitAnon").GetInt32());
    }

    [Fact]
    // Lần sửa thứ hai ghi trạng thái trước bằng giá trị của lần sửa thứ nhất.
    public async Task UpdateAsync_RecordsPreviousValuesAsBefore()
    {
        await DeleteSettingsRowAsync();
        var actorId = $"test-admin-{Guid.NewGuid():N}";
        var store = CreateStore();
        await store.UpdateAsync(new AiSettings(40, 60, 10), actorId, CancellationToken.None);

        await store.UpdateAsync(new AiSettings(45, 60, 10), actorId, CancellationToken.None);

        await using var identity = database.CreateIdentityContext();
        var audits = await identity.AuditLogs.AsNoTracking()
            .Where(log => log.ActorId == actorId)
            .OrderBy(log => log.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Equal(40, audits[1].Before!.RootElement.GetProperty("monthlyBudgetUsd").GetDouble());
        Assert.Equal(45, audits[1].After!.RootElement.GetProperty("monthlyBudgetUsd").GetDouble());
    }

    [Theory]
    [InlineData(-10, 50, 15)]
    [InlineData(double.PositiveInfinity, 50, 15)]
    [InlineData(30, 0, 15)]
    [InlineData(30, 50, -1)]
    // Giá trị âm hoặc không hữu hạn bị từ chối. DB không có dòng audit rác.
    public async Task UpdateAsync_RejectsInvalidValuesWithoutAudit(double budget, int userLimit, int anonLimit)
    {
        var actorId = $"test-admin-{Guid.NewGuid():N}";

        var error = await Assert.ThrowsAsync<AppException>(
            () => CreateStore().UpdateAsync(new AiSettings(budget, userLimit, anonLimit), actorId, CancellationToken.None));

        Assert.Equal(("INVALID_AI_SETTINGS", 400), (error.Code, error.StatusCode));
        Assert.Contains("ngân sách phải không âm", error.Message);
        Assert.Equal(0, await CountSettingsAuditRowsAsync(actorId));
    }

    [Fact]
    // Ghi audit lỗi thì cấu hình mới cũng bị hủy. Không có thay đổi nào thiếu nhật ký.
    public async Task UpdateAsync_AuditFailureRollsBackSettings()
    {
        await DeleteSettingsRowAsync();
        await CreateStore().UpdateAsync(new AiSettings(30, 50, 15), "setup-actor", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateStore(new FailingAuditLogWriter()).UpdateAsync(new AiSettings(99, 50, 15), "actor", CancellationToken.None));

        await using var aiGateway = database.CreateAiGatewayContext();
        Assert.Equal(30, (await aiGateway.Settings.AsNoTracking().SingleAsync()).MonthlyBudgetUsd);
    }

    // Bản giả ném lỗi sau khi nhận transaction. Test kiểm tra cấu hình không được lưu riêng.
    private sealed class FailingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Ghi audit thất bại.");
        }
    }
}
