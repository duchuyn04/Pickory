using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Audit;
using Xunit;

namespace Pickory.IntegrationTests.Database;

// Port từ test ID-04 của bản Node. Nhật ký thao tác đi cùng giao dịch của thao tác chính.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AuditRecorderTests(IntegrationDatabaseFixture database)
{
    [Fact]
    // Thao tác chính bị hủy thì nhật ký cũng không còn. Nhật ký không ghi thao tác chưa từng xảy ra.
    public async Task Add_RolledBackTransactionLeavesNoRow()
    {
        var targetId = Guid.NewGuid().ToString();
        await using var identity = database.CreateIdentityContext();

        // Ghi nhật ký trong giao dịch, rồi hủy giao dịch như khi thao tác chính thất bại.
        await using (var transaction = await identity.Database.BeginTransactionAsync())
        {
            AuditRecorder.Add(identity, "actor-1", "role.assign", "user", targetId, before: null, after: new { roles = new[] { "sales" } });
            await identity.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verify = database.CreateIdentityContext();
        Assert.False(await verify.AuditLogs.AnyAsync(log => log.TargetId == targetId));
    }

    [Fact]
    // Giao dịch thành công thì nhật ký lưu đủ người thực hiện, hành động, đối tượng và trạng thái sau.
    public async Task Add_CommittedTransactionStoresSanitizedAfterAndJsonNullBefore()
    {
        var targetId = Guid.NewGuid().ToString();
        await using (var identity = database.CreateIdentityContext())
        {
            AuditRecorder.Add(identity, "actor-1", "role.assign", "user", targetId, before: null, after: new { roles = new[] { "sales" }, newPassword = "secret-value" });
            await identity.SaveChangesAsync();
        }

        await using var verify = database.CreateIdentityContext();
        var log = await verify.AuditLogs.AsNoTracking().SingleAsync(value => value.TargetId == targetId);
        Assert.Equal("actor-1", log.ActorId);
        Assert.Equal("role.assign", log.Action);
        Assert.Equal("user", log.TargetType);
        // Mật khẩu mới bị giấu trước khi chạm DB.
        Assert.Equal("[REDACTED]", log.After!.RootElement.GetProperty("newPassword").GetString());
        Assert.Equal("sales", log.After.RootElement.GetProperty("roles")[0].GetString());
        // Bản Node lưu `Prisma.JsonNull` khi thiếu trạng thái. Cột chứa JSON `null`, không phải SQL `NULL`.
        var beforeIsJsonNull = await verify.Database
            .SqlQuery<bool>($"SELECT \"before\" = 'null'::jsonb AS \"Value\" FROM audit_logs WHERE \"targetId\" = {targetId}")
            .SingleAsync();
        Assert.True(beforeIsJsonNull);
        Assert.Equal(JsonValueKind.Null, log.Before!.RootElement.ValueKind);
    }
}
