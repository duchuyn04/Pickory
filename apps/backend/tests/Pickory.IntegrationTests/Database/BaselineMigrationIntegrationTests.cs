using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class BaselineMigrationIntegrationTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private static readonly string[] HistorySchemas = ["ef_identity", "ef_ai_gateway", "ef_catalog", "ef_shipping", "ef_commerce"];

    [Theory]
    [InlineData("identity", "ef_identity", "20261013101735_EnableUnaccentExtension")]
    [InlineData("ai_gateway", "ef_ai_gateway", "20261005125642_PrismaBaseline")]
    [InlineData("catalog", "ef_catalog", "20261013124840_EnableUnaccentExtension")]
    [InlineData("shipping", "ef_shipping", "20261019123657_AddShippingQuoteCache")]
    [InlineData("commerce", "ef_commerce", "20261019144909_AddOrderShippingFeeSource")]
    public async Task Migration_DownRemovesOnlyOwnedObjectsAndUpRestoresThemWithIsolatedHistory(
        string module, string historySchema, string migrationId)
    {
        // Context từ đăng ký dịch vụ phải dùng đúng DB và cấu hình lịch sử module thật.
        await using var scope = factory.Services.CreateAsyncScope();
        DbContext context = module switch
        {
            "identity" => scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            "ai_gateway" => scope.ServiceProvider.GetRequiredService<AiGatewayDbContext>(),
            "catalog" => scope.ServiceProvider.GetRequiredService<CatalogDbContext>(),
            "shipping" => scope.ServiceProvider.GetRequiredService<ShippingDbContext>(),
            "commerce" => scope.ServiceProvider.GetRequiredService<CommerceDbContext>(),
            _ => throw new ArgumentOutOfRangeException(nameof(module))
        };
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        // `migrationId` là migration mới nhất của module. Module có nhiều migration thì script Down và Up chạy qua tất cả migration.
        var migrationIds = migrationsAssembly.Migrations.Keys.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(migrationId, migrationIds[^1]);
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
        Assert.Equal(migrationIds, AppliedIds(await historyRepository.GetAppliedMigrationsAsync()));
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
            Assert.Equal(migrationIds, AppliedIds(await historyRepository.GetAppliedMigrationsAsync()));
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

    [Fact]
    public async Task CatalogMigration_EmptySchemaCreatesNonUniqueOrderMovementIndex()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        var migrator = context.GetService<IMigrator>();
        var latest = context.GetService<IMigrationsAssembly>().Migrations.Keys.Order(StringComparer.Ordinal).Last();
        // Rollback giữ nguyên fixture khi kiểm schema Catalog rỗng qua SQL migration thật.
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync(migrator.GenerateScript(latest, Migration.InitialDatabase, MigrationsSqlGenerationOptions.NoTransactions));
            await context.Database.ExecuteSqlRawAsync(migrator.GenerateScript(Migration.InitialDatabase, latest, MigrationsSqlGenerationOptions.NoTransactions));
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            // Catalog PostgreSQL chứng minh index mới tồn tại mà không còn chặn chu kỳ giữ hàng.
            command.CommandText = """
                SELECT indexname, indexdef FROM pg_indexes
                WHERE schemaname = 'public' AND indexname IN
                ('inventory_movements_orderId_skuId_type_key', 'inventory_movements_orderId_skuId_type_idx')
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("inventory_movements_orderId_skuId_type_idx", reader.GetString(0));
            Assert.DoesNotContain("UNIQUE", reader.GetString(1));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static List<string> AppliedIds(IReadOnlyList<HistoryRow> rows) =>
        rows.Select(row => row.MigrationId).Order(StringComparer.Ordinal).ToList();

    private static bool IsOwnedObject(string key, string module)
    {
        var tables = module switch
        {
            "identity" => ApplicationDatabaseSchema.IdentityTables,
            "ai_gateway" => ApplicationDatabaseSchema.AiGatewayTables,
            "catalog" => ApplicationDatabaseSchema.CatalogTables,
            "shipping" => ApplicationDatabaseSchema.ShippingTables,
            "commerce" => ApplicationDatabaseSchema.CommerceTables,
            _ => throw new ArgumentOutOfRangeException(nameof(module))
        };
        var enums = module switch
        {
            "identity" => new[] { "UserKind", "EmailTokenPurpose" },
            "catalog" => new[] { "ProductStatus", "InventoryMovementType" },
            "shipping" => ApplicationDatabaseSchema.ShippingEnums,
            "commerce" => ApplicationDatabaseSchema.CommerceEnums,
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
