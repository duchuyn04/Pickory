using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Api.Deployment;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Database;

// Bước dọn của `db:migrate` xóa ba bảng và schema lịch sử còn sót từ module AI đã gỡ.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AiGatewayCleanupTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private static readonly string[] LegacyTables = ["ai_usage_logs", "ai_settings", "ai_embedding_cache"];

    [Fact]
    // DB cũ còn ba bảng AI và schema `ef_ai_gateway`. Sau `db:migrate` chúng biến mất, dữ liệu module khác giữ nguyên, và lần chạy thứ hai không đổi gì.
    public async Task Migrate_DropsLegacyAiObjectsKeepsOtherDataAndIsRepeatable()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        // Dựng lại hiện trạng DB cũ bằng bảng rỗng. Test chỉ cần tên bảng và tên schema, không cần cột thật của module AI.
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS public.ai_usage_logs (id text PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS public.ai_settings (id text PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS public.ai_embedding_cache (id text PRIMARY KEY);
            CREATE SCHEMA IF NOT EXISTS ef_ai_gateway;
            CREATE TABLE IF NOT EXISTS ef_ai_gateway."__EFMigrationsHistory" ("MigrationId" text PRIMARY KEY);
            """);
        // Một tài khoản thật của module identity. Bước dọn không được xóa hay sửa dữ liệu này.
        var user = await IdentityTestData.CreateUserAsync(database, IdentityTestData.NewEmail("ai-cleanup"), UserKind.Staff, "sales");

        var firstRun = await RunMigrateAsync();

        Assert.Equal(0, firstRun.ExitCode);
        Assert.Contains("Migration AiGatewayCleanup đã áp xong.", firstRun.Output);
        var afterFirstRun = await ReadStateAsync(connection);
        Assert.Empty(afterFirstRun.LegacyTables);
        Assert.False(afterFirstRun.LegacySchemaExists);
        await using (var identity = database.CreateIdentityContext())
        {
            Assert.True(await identity.Users.AnyAsync(existing => existing.Id == user.Id));
        }

        // Chạy lần hai trên DB đã sạch. `IF EXISTS` giúp lệnh không lỗi, và danh sách đối tượng không đổi.
        var secondRun = await RunMigrateAsync();

        Assert.Equal(0, secondRun.ExitCode);
        Assert.Equal(afterFirstRun, await ReadStateAsync(connection));
    }

    private async Task<(int ExitCode, string Output)> RunMigrateAsync()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await DatabaseMigrationCommand.RunAsync(factory.Services, output, error, CancellationToken.None);
        Assert.Empty(error.ToString());
        return (exitCode, output.ToString());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<LegacyState> ReadStateAsync(NpgsqlConnection connection)
    {
        await using var tableCommand = new NpgsqlCommand(
            "SELECT relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE n.nspname = 'public' AND c.relkind = 'r' AND c.relname = ANY(@names) ORDER BY relname", connection);
        tableCommand.Parameters.AddWithValue("names", LegacyTables);
        var tables = new List<string>();
        await using (var reader = await tableCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        await using var schemaCommand = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'ef_ai_gateway')", connection);
        var schemaExists = (bool)(await schemaCommand.ExecuteScalarAsync())!;
        return new LegacyState(tables, schemaExists);
    }

    private sealed record LegacyState(List<string> LegacyTables, bool LegacySchemaExists)
    {
        // So sánh theo nội dung danh sách thay vì tham chiếu, để hai lần đọc giống nhau thì bằng nhau.
        public bool Equals(LegacyState? other) =>
            other is not null && LegacySchemaExists == other.LegacySchemaExists && LegacyTables.SequenceEqual(other.LegacyTables);

        public override int GetHashCode() => HashCode.Combine(LegacySchemaExists, LegacyTables.Count);
    }
}
