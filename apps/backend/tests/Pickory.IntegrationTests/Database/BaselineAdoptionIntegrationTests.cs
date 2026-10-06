using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class BaselineAdoptionIntegrationTests(IntegrationDatabaseFixture database)
{
    [Fact]
    public async Task Adoption_StampsAllModuleHistoriesIdempotentlyWithoutChangingExistingSchemaOrRecords()
    {
        // Giao dịch giữ dữ liệu hiện có và schema fixture sau bài kiểm thử.
        await using var context = database.CreateIdentityContext();
        var originalSchema = await ApplicationDatabaseSchema.ReadAsync(context);
        var originalHistory = await BaselineMigrationIntegrationTests.ReadHistoryAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            // Bản ghi thật chứng minh việc nhận schema không chạy SQL tạo bảng hoặc thay thế dữ liệu.
            var user = new User
            {
                Email = $"adoption-{Guid.NewGuid():N}@example.test",
                Kind = UserKind.Staff,
                Name = "Dữ liệu hiện có",
                UpdatedAt = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc)
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();
            var userId = user.Id;

            // Schema ứng dụng đã có. DB chưa có lịch sử Entity Framework Core của bất kỳ module nào.
            await context.Database.ExecuteSqlRawAsync("""
                DROP SCHEMA ef_identity CASCADE;
                DROP SCHEMA ef_ai_gateway CASCADE;
                DROP SCHEMA ef_catalog CASCADE;
                """);
            var script = DatabaseSqlFiles.Load("adopt-existing-schema.sql", removeTransaction: true);
            await context.Database.ExecuteSqlRawAsync(script);
            var stamped = await BaselineMigrationIntegrationTests.ReadHistoryAsync(context);
            Assert.Contains("20261005125639_PrismaBaseline|", stamped["ef_identity"]);
            Assert.Contains("20261005125642_PrismaBaseline|", stamped["ef_ai_gateway"]);
            Assert.Contains("20261005125646_PrismaBaseline|", stamped["ef_catalog"]);
            Assert.All(stamped.Values, rows => Assert.Single(rows.Split('\n')));
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());

            // Lần nhận schema thứ hai phải giữ nguyên lịch sử đã ghi, không thêm dòng hoặc đổi phiên bản.
            await context.Database.ExecuteSqlRawAsync(script);
            BaselineMigrationIntegrationTests.AssertHistoriesEqual(stamped,
                await BaselineMigrationIntegrationTests.ReadHistoryAsync(context));
            ApplicationDatabaseSchema.AssertEqual(originalSchema, await ApplicationDatabaseSchema.ReadAsync(context));

            // Đọc mới từ PostgreSQL để tránh giá trị được giữ trong bộ theo dõi Entity Framework Core.
            context.ChangeTracker.Clear();
            var persisted = await context.Users.SingleAsync(existing => existing.Id == userId);
            Assert.Equal(user.Email, persisted.Email);
            Assert.Equal(user.Name, persisted.Name);
            Assert.Equal(user.Kind, persisted.Kind);
            Assert.Equal(user.UpdatedAt, persisted.UpdatedAt);
        }
        finally
        {
            // Rollback khôi phục cả dữ liệu thử và lịch sử ban đầu khi assertion lỗi.
            await transaction.RollbackAsync();
        }
        ApplicationDatabaseSchema.AssertEqual(originalSchema, await ApplicationDatabaseSchema.ReadAsync(context));
        BaselineMigrationIntegrationTests.AssertHistoriesEqual(originalHistory,
            await BaselineMigrationIntegrationTests.ReadHistoryAsync(context));
    }

    [Theory]
    [InlineData("DROP TABLE public.ai_embedding_cache", "thiếu bảng")]
    [InlineData("ALTER TYPE public.\"UserKind\" RENAME VALUE 'staff' TO 'unknown'", "Enum")]
    [InlineData("DROP EXTENSION vector", "vector")]
    public async Task Adoption_RefusesIncompleteOrIncompatibleSchemaWithoutStampingHistory(string invalidation, string expectedError)
    {
        // Lưu schema trước giao dịch để chứng minh lỗi điều kiện tiên quyết không để lại thay đổi.
        await using var context = database.CreateIdentityContext();
        var originalSchema = await ApplicationDatabaseSchema.ReadAsync(context);
        var originalHistory = await BaselineMigrationIntegrationTests.ReadHistoryAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            // Xóa lịch sử trước khi gây sai lệch để phát hiện việc ghi nhận baseline không hợp lệ.
            await context.Database.ExecuteSqlRawAsync("""
                DROP SCHEMA ef_identity CASCADE;
                DROP SCHEMA ef_ai_gateway CASCADE;
                DROP SCHEMA ef_catalog CASCADE;
                """);
            await context.Database.ExecuteSqlRawAsync(invalidation);
            var invalidSchema = await ApplicationDatabaseSchema.ReadAsync(context);
            await transaction.CreateSavepointAsync("before_adoption");

            // Script thật phải báo lỗi PostgreSQL trước bước ghi lịch sử.
            var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
                DatabaseSqlFiles.Load("adopt-existing-schema.sql", removeTransaction: true)));
            Assert.Equal("P0001", error.SqlState);
            Assert.Contains(expectedError, error.MessageText);

            // Savepoint cho phép kiểm tra trạng thái sau lỗi SQL mà không hủy toàn bộ giao dịch thử.
            await transaction.RollbackToSavepointAsync("before_adoption");
            ApplicationDatabaseSchema.AssertEqual(invalidSchema, await ApplicationDatabaseSchema.ReadAsync(context));
            Assert.All(await BaselineMigrationIntegrationTests.ReadHistoryAsync(context), entry => Assert.Equal("<missing>", entry.Value));
        }
        finally
        {
            // Rollback phục hồi schema hợp lệ cho các bài kiểm thử còn lại.
            await transaction.RollbackAsync();
        }
        ApplicationDatabaseSchema.AssertEqual(originalSchema, await ApplicationDatabaseSchema.ReadAsync(context));
        BaselineMigrationIntegrationTests.AssertHistoriesEqual(originalHistory,
            await BaselineMigrationIntegrationTests.ReadHistoryAsync(context));
    }
}
