using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Identity.Persistence;
using Xunit;

namespace Pickory.IntegrationTests;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class BaselineMigrationIntegrationTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private static readonly string[] HistorySchemas = ["ef_identity", "ef_ai_gateway", "ef_catalog"];

    [Theory]
    [InlineData("identity", "ef_identity", "20261005125639_PrismaBaseline")]
    [InlineData("ai_gateway", "ef_ai_gateway", "20261005125642_PrismaBaseline")]
    [InlineData("catalog", "ef_catalog", "20261005125646_PrismaBaseline")]
    public async Task Baseline_DownRemovesOnlyOwnedObjectsAndUpRestoresThemWithIsolatedHistory(
        string module, string historySchema, string migrationId)
    {
        // Context từ đăng ký dịch vụ phải dùng đúng DB và cấu hình lịch sử module thật.
        await using var scope = factory.Services.CreateAsyncScope();
        DbContext context = module switch
        {
            "identity" => scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            "ai_gateway" => scope.ServiceProvider.GetRequiredService<AiGatewayDbContext>(),
            "catalog" => scope.ServiceProvider.GetRequiredService<CatalogDbContext>(),
            _ => throw new ArgumentOutOfRangeException(nameof(module))
        };
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var baseline = Assert.Single(migrationsAssembly.Migrations);
        Assert.Equal(migrationId, baseline.Key);
        Assert.NotNull(migrationsAssembly.ModelSnapshot);
        Assert.False(context.Database.HasPendingModelChanges(), $"{context.GetType().Name} snapshot must match its design-time model.");
        var migrator = context.GetService<IMigrator>();
        var downSql = migrator.GenerateScript(migrationId, Migration.InitialDatabase, MigrationsSqlGenerationOptions.NoTransactions);
        var upSql = migrator.GenerateScript(Migration.InitialDatabase, migrationId, MigrationsSqlGenerationOptions.NoTransactions);

        // Lưu trạng thái đầy đủ để chứng minh rollback không để lại thay đổi schema hoặc lịch sử.
        var original = await ApplicationDatabaseSchema.ReadAsync(context);
        var originalHistory = await ReadHistoryAsync(context);
        Assert.All(originalHistory, entry => Assert.NotEqual("<missing>", entry.Value));
        var historyRepository = context.GetService<IHistoryRepository>();
        Assert.Equal(migrationId, Assert.Single(await historyRepository.GetAppliedMigrationsAsync()).MigrationId);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            // SQL Down thật phải xóa bảng và enum sở hữu mà không thay đổi module khác.
            await context.Database.ExecuteSqlRawAsync(downSql);
            var afterDown = await ApplicationDatabaseSchema.ReadAsync(context);
            var retainedObjects = original.Where(entry => !IsOwnedObject(entry.Key, module))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            ApplicationDatabaseSchema.AssertEqual(retainedObjects, afterDown);
            Assert.Empty(await historyRepository.GetAppliedMigrationsAsync());
            AssertOtherHistoriesUnchanged(originalHistory, await ReadHistoryAsync(context), historySchema);

            // SQL Up thật phải tạo lại đầy đủ đối tượng trên phần schema module đang rỗng.
            await context.Database.ExecuteSqlRawAsync(upSql);
            ApplicationDatabaseSchema.AssertEqual(original, await ApplicationDatabaseSchema.ReadAsync(context));
            Assert.Equal(migrationId, Assert.Single(await historyRepository.GetAppliedMigrationsAsync()).MigrationId);
            AssertOtherHistoriesUnchanged(originalHistory, await ReadHistoryAsync(context), historySchema);
        }
        finally
        {
            // Rollback giữ schema fixture cho API và bài kiểm thử đọc ghi tiếp theo.
            await transaction.RollbackAsync();
        }
        AssertHistoriesEqual(originalHistory, await ReadHistoryAsync(context));
        ApplicationDatabaseSchema.AssertEqual(original, await ApplicationDatabaseSchema.ReadAsync(context));
    }

    private static bool IsOwnedObject(string key, string module)
    {
        var tables = module switch
        {
            "identity" => ApplicationDatabaseSchema.IdentityTables,
            "ai_gateway" => ApplicationDatabaseSchema.AiGatewayTables,
            "catalog" => ApplicationDatabaseSchema.CatalogTables,
            _ => throw new ArgumentOutOfRangeException(nameof(module))
        };
        var enums = module switch
        {
            "identity" => new[] { "UserKind", "EmailTokenPurpose" },
            "catalog" => new[] { "ProductStatus", "InventoryMovementType" },
            _ => Array.Empty<string>()
        };
        // Ranh giới tên ngăn `users` vô tình khớp bảng có tiền tố tương tự.
        return tables.Any(table => key.EndsWith($":public.{table}", StringComparison.Ordinal) ||
                                   key.Contains($":public.{table}.", StringComparison.Ordinal)) ||
               enums.Any(name => key.EndsWith($":public.{name}", StringComparison.Ordinal)) ||
               (module == "ai_gateway" && key == "extension:vector");
    }

    internal static async Task<Dictionary<string, string>> ReadHistoryAsync(DbContext context)
    {
        // Mở kết nối trước lệnh catalog để helper dùng được ngoài giao dịch.
        await context.Database.OpenConnectionAsync();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var schema in HistorySchemas)
        {
            // `to_regclass` phân biệt lịch sử chưa tồn tại với lịch sử rỗng.
            await using var existence = context.Database.GetDbConnection().CreateCommand();
            existence.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            existence.CommandText = "SELECT to_regclass(@qualified_name)::text";
            var parameter = existence.CreateParameter();
            parameter.ParameterName = "qualified_name";
            parameter.Value = $"{schema}.\"__EFMigrationsHistory\"";
            existence.Parameters.Add(parameter);
            if (await existence.ExecuteScalarAsync() is null or DBNull)
            {
                result.Add(schema, "<missing>");
                continue;
            }

            // Tên schema cố định không nhận đầu vào SQL bên ngoài.
            await using var rowsCommand = context.Database.GetDbConnection().CreateCommand();
            rowsCommand.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            rowsCommand.CommandText = $"SELECT \"MigrationId\", \"ProductVersion\" FROM {schema}.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\"";
            await using var reader = await rowsCommand.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}");
            }
            result.Add(schema, string.Join("\n", rows));
        }
        return result;
    }

    private static void AssertOtherHistoriesUnchanged(IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after, string changedSchema)
    {
        foreach (var schema in HistorySchemas.Where(schema => schema != changedSchema))
        {
            Assert.Equal(before[schema], after[schema]);
        }
    }

    internal static void AssertHistoriesEqual(IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        foreach (var schema in HistorySchemas)
        {
            Assert.Equal(expected[schema], actual[schema]);
        }
    }
}
