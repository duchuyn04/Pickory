using System.Text.RegularExpressions;
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

public sealed class BaselineMigrationIntegrationTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private static readonly string[] HistorySchemas = ["ef_identity", "ef_catalog", "ef_ai_gateway"];

    [Theory]
    [InlineData("identity", "ef_identity")]
    [InlineData("catalog", "ef_catalog")]
    [InlineData("ai_gateway", "ef_ai_gateway")]
    public async Task Baseline_ApplyAndDownOnlyChangeIsolatedHistoryAndRollbackEverything(string module, string historySchema)
    {
        // Context từ DI bảo đảm lịch sử migration dùng cùng cấu hình với ứng dụng thật.
        await using var scope = factory.Services.CreateAsyncScope();
        DbContext context = module switch
        {
            "identity" => scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            "catalog" => scope.ServiceProvider.GetRequiredService<CatalogDbContext>(),
            "ai_gateway" => scope.ServiceProvider.GetRequiredService<AiGatewayDbContext>(),
            _ => throw new ArgumentOutOfRangeException(nameof(module))
        };
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await PrismaDatabaseSchema.RequireMigratedSchemaAsync(context);
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var baselineEntry = Assert.Single(migrationsAssembly.Migrations);
        var baseline = migrationsAssembly.CreateMigration(baselineEntry.Value, context.Database.ProviderName!);

        // Mốc chỉ đánh dấu schema có sẵn. Không thêm DDL (Data Definition Language) cho ứng dụng.
        Assert.Empty(baseline.UpOperations);
        Assert.Empty(baseline.DownOperations);
        Assert.NotNull(migrationsAssembly.ModelSnapshot);
        Assert.False(context.Database.HasPendingModelChanges(),
            $"{context.GetType().Name} baseline snapshot must match its design-time model.");
        var migrator = context.GetService<IMigrator>();
        var upSql = migrator.GenerateScript(Migration.InitialDatabase, baselineEntry.Key,
            MigrationsSqlGenerationOptions.NoTransactions);
        var downSql = migrator.GenerateScript(baselineEntry.Key, Migration.InitialDatabase,
            MigrationsSqlGenerationOptions.NoTransactions);
        var historyRepository = context.GetService<IHistoryRepository>();
        var createHistorySql = historyRepository.GetCreateIfNotExistsScript();
        AssertHistoryOnlySql(upSql, historySchema);
        AssertHistoryOnlySql(downSql, historySchema);
        AssertHistoryOnlySql(createHistorySql, historySchema);
        Assert.Contains($"{historySchema}.\"__EFMigrationsHistory\"", upSql);
        Assert.Contains($"{historySchema}.\"__EFMigrationsHistory\"", downSql);

        // So sánh trạng thái trước giao dịch để kiểm tra rollback khôi phục cả lịch sử có sẵn.
        var originalApplicationSchema = await PrismaDatabaseSchema.ReadAsync(context);
        var originalHistory = await ReadHistoryAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            // Tạo riêng lịch sử EF được phép; schema Prisma không được tạo hay migrate bởi bài kiểm thử.
            await context.Database.ExecuteSqlRawAsync(createHistorySql);
            await context.Database.ExecuteSqlRawAsync(historyRepository.GetDeleteScript(baselineEntry.Key));
            var historyBeforeApply = await ReadHistoryAsync(context);

            // SQL thực của provider phải áp dụng được khi baseline chưa được đánh dấu trong lịch sử.
            await context.Database.ExecuteSqlRawAsync(upSql);
            var historyAfterApply = await ReadHistoryAsync(context);
            var appliedRows = await historyRepository.GetAppliedMigrationsAsync();
            var appliedBaseline = Assert.Single(appliedRows, row => row.MigrationId == baselineEntry.Key);
            Assert.False(string.IsNullOrWhiteSpace(appliedBaseline.ProductVersion));
            AssertOtherHistoriesUnchanged(historyBeforeApply, historyAfterApply, historySchema);
            PrismaDatabaseSchema.AssertEqual(originalApplicationSchema,
                await PrismaDatabaseSchema.ReadAsync(context));

            // SQL đảo baseline chỉ xóa dấu lịch sử, không xóa bảng hoặc enum do Prisma sở hữu.
            await context.Database.ExecuteSqlRawAsync(downSql);
            var historyAfterDown = await ReadHistoryAsync(context);
            Assert.DoesNotContain(await historyRepository.GetAppliedMigrationsAsync(),
                row => row.MigrationId == baselineEntry.Key);
            Assert.Equal(historyBeforeApply[historySchema], historyAfterDown[historySchema]);
            AssertOtherHistoriesUnchanged(historyBeforeApply, historyAfterDown, historySchema);
            PrismaDatabaseSchema.AssertEqual(originalApplicationSchema,
                await PrismaDatabaseSchema.ReadAsync(context));
        }
        finally
        {
            // Hủy giao dịch ngay cả khi assertion lỗi để không để lại lịch sử hoặc schema EF kiểm thử.
            await transaction.RollbackAsync();
        }

        AssertHistoriesEqual(originalHistory, await ReadHistoryAsync(context));
        PrismaDatabaseSchema.AssertEqual(originalApplicationSchema,
            await PrismaDatabaseSchema.ReadAsync(context));
    }

    private static void AssertHistoryOnlySql(string sql, string historySchema)
    {
        // Các thao tác DDL chỉ được nhắm schema lịch sử hoặc bảng lịch sử của module hiện tại.
        var ddlTargets = Regex.Matches(sql,
            "\\b(?:CREATE|ALTER|DROP)\\s+(?:TABLE|SCHEMA|TYPE|INDEX|EXTENSION)\\s+" +
            "(?:IF\\s+(?:NOT\\s+)?EXISTS\\s+)?(?<target>(?:\"[^\"]+\"|\\w+)(?:\\.(?:\"[^\"]+\"|\\w+))?)",
            RegexOptions.IgnoreCase);
        foreach (Match match in ddlTargets)
        {
            var target = match.Groups["target"].Value.Replace("\"", "", StringComparison.Ordinal);
            Assert.True(target == historySchema || target == $"{historySchema}.__EFMigrationsHistory",
                $"Baseline SQL must not change application DDL: {match.Value}");
        }

        foreach (var otherSchema in HistorySchemas.Where(schema => schema != historySchema))
        {
            Assert.DoesNotContain(otherSchema, sql);
        }

        // Không cho phép tạo extension hoặc tác động đến enum ngay cả qua SQL sinh tự động.
        Assert.DoesNotMatch("(?i)\\b(?:CREATE|ALTER|DROP)\\s+(?:TYPE|EXTENSION)\\b", sql);
    }

    private static async Task<Dictionary<string, string>> ReadHistoryAsync(DbContext context)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var schema in HistorySchemas)
        {
            // `to_regclass` phân biệt lịch sử chưa tồn tại với bảng lịch sử rỗng.
            await using var existenceCommand = context.Database.GetDbConnection().CreateCommand();
            existenceCommand.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            existenceCommand.CommandText = "SELECT to_regclass(@qualified_name)::text";
            var parameter = existenceCommand.CreateParameter();
            parameter.ParameterName = "qualified_name";
            parameter.Value = $"{schema}.\"__EFMigrationsHistory\"";
            existenceCommand.Parameters.Add(parameter);
            var tableName = await existenceCommand.ExecuteScalarAsync();
            if (tableName is null or DBNull)
            {
                result.Add(schema, "<missing>");
                continue;
            }

            // Danh sách schema cố định giữ tên SQL an toàn, không nhận tên từ đầu vào người dùng.
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
            Assert.True(before[schema] == after[schema], $"Baseline must not modify {schema} history.");
        }
    }

    private static void AssertHistoriesEqual(IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        foreach (var schema in HistorySchemas)
        {
            Assert.True(expected[schema] == actual[schema], $"Rollback must restore {schema} history exactly.");
        }
    }
}
