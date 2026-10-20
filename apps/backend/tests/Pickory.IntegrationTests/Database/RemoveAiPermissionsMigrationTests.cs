using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Setup;
using Xunit;

namespace Pickory.IntegrationTests.Database;

// Migration dữ liệu của identity xóa dòng quyền AI còn sót trong `role_permissions` khi module AI bị gỡ.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class RemoveAiPermissionsMigrationTests(IntegrationDatabaseFixture database)
{
    private const string MigrationName = "RemoveAiPermissions";

    [Fact]
    // DB cũ có role mang quyền AI. Sau migration chỉ mất đúng hai dòng quyền AI, và bước kiểm khi khởi động không còn cảnh báo chúng.
    public async Task Up_DeletesOnlyTheTwoAiPermissionRowsAndStartupCheckNoLongerWarnsAboutThem()
    {
        await using var identity = database.CreateIdentityContext();
        var migrationIds = identity.GetService<IMigrationsAssembly>().Migrations.Keys.Order(StringComparer.Ordinal).ToList();
        var cleanupId = migrationIds.Single(id => id.EndsWith($"_{MigrationName}", StringComparison.Ordinal));
        var previousId = migrationIds[migrationIds.IndexOf(cleanupId) - 1];
        var migrator = identity.GetService<IMigrator>();
        var downSql = migrator.GenerateScript(cleanupId, previousId, MigrationsSqlGenerationOptions.NoTransactions);
        var upSql = migrator.GenerateScript(previousId, cleanupId, MigrationsSqlGenerationOptions.NoTransactions);

        // Giao dịch bao cả dữ liệu thử và hai lần chạy script, nên rollback trả DB fixture về nguyên trạng.
        await using var transaction = await identity.Database.BeginTransactionAsync();
        try
        {
            // Hoàn tác migration để lịch sử không còn dòng của nó. Khi đó script Up chạy lại được như trên DB cũ.
            await identity.Database.ExecuteSqlRawAsync(downSql);
            var role = new Role { Code = $"ai-cleanup-{Guid.NewGuid():N}", Name = "Role còn quyền AI" };
            foreach (var permission in new[] { "ai-usage:read", "ai-settings:manage", "orders:read", "legacy:other" })
            {
                role.Permissions.Add(new RolePermission { Permission = permission });
            }
            identity.Roles.Add(role);
            await identity.SaveChangesAsync();

            // Trước migration, bước kiểm khi khởi động phải thấy hai quyền AI là quyền lạ. Nếu không thì phần kiểm sau không có giá trị.
            var warningsBefore = new RecordingLogger();
            await StoredPermissionCheck.WarnAboutUnknownPermissionsAsync(identity, warningsBefore, CancellationToken.None);
            Assert.Contains(warningsBefore.Warnings, warning => warning.Contains("ai-usage:read", StringComparison.Ordinal));
            Assert.Contains(warningsBefore.Warnings, warning => warning.Contains("ai-settings:manage", StringComparison.Ordinal));

            await identity.Database.ExecuteSqlRawAsync(upSql);

            // Quyền hợp lệ và quyền lạ khác không thuộc phạm vi dọn dẹp, nên phải còn nguyên.
            identity.ChangeTracker.Clear();
            var remaining = await identity.RolePermissions
                .Where(rolePermission => rolePermission.RoleId == role.Id)
                .Select(rolePermission => rolePermission.Permission)
                .OrderBy(permission => permission)
                .ToListAsync();
            Assert.Equal(["legacy:other", "orders:read"], remaining);

            var warningsAfter = new RecordingLogger();
            await StoredPermissionCheck.WarnAboutUnknownPermissionsAsync(identity, warningsAfter, CancellationToken.None);
            Assert.DoesNotContain(warningsAfter.Warnings, warning => warning.Contains("ai-usage:read", StringComparison.Ordinal));
            Assert.DoesNotContain(warningsAfter.Warnings, warning => warning.Contains("ai-settings:manage", StringComparison.Ordinal));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
