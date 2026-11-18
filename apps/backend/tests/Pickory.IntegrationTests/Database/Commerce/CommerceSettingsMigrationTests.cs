using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database.Commerce;

// Kiểm tra migration thêm bốn cột thông tin shop trên DB sạch và DB có sẵn dòng cấu hình.
[Collection(DatabaseCollection.Name)]
public sealed class CommerceSettingsMigrationTests(DatabaseWebApplicationFactory factory)
    : IClassFixture<DatabaseWebApplicationFactory>
{
    private const string PreviousMigrationId = "20261030050733_AddSavedAddressSoftDelete";
    private const string ShopMigrationId = "20261118174514_AddCommerceSettingsShopInfo";

    [Fact]
    // Migration chạy trên DB sạch tạo đủ bốn cột shop kiểu `text`, cho phép rỗng và mặc định `null`.
    public async Task CommerceSettingsMigration_OnCleanSchema_CreatesNullableShopColumns()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        var migrator = context.GetService<IMigrator>();
        var downToInitial = migrator.GenerateScript(
            ShopMigrationId, Migration.InitialDatabase, MigrationsSqlGenerationOptions.NoTransactions);
        var upToLatest = migrator.GenerateScript(
            Migration.InitialDatabase, ShopMigrationId, MigrationsSqlGenerationOptions.NoTransactions);

        // Dùng transaction ngoài để rollback sạch sau khi kiểm tra migration trên schema trống.
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync(downToInitial);
            await context.Database.ExecuteSqlRawAsync(upToLatest);

            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                SELECT column_name, data_type, is_nullable, column_default
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'commerce_settings'
                  AND column_name IN ('shopName', 'shopAddress', 'shopPhone', 'shopTaxCode')
                ORDER BY column_name
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<(string Name, string Type, string Nullable, bool HasDefault)>();
            while (await reader.ReadAsync())
            {
                columns.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    !reader.IsDBNull(3)));
            }

            Assert.Equal(
                [
                    ("shopAddress", "text", "YES", false),
                    ("shopName", "text", "YES", false),
                    ("shopPhone", "text", "YES", false),
                    ("shopTaxCode", "text", "YES", false),
                ],
                columns);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    // Migration chạy trên DB có sẵn một dòng phải giữ nguyên giá trị cũ và đặt bốn cột mới là `null`.
    public async Task CommerceSettingsMigration_WithExistingRow_PreservesOldValuesAndSetsShopNull()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        var migrator = context.GetService<IMigrator>();
        var downOne = migrator.GenerateScript(
            ShopMigrationId, PreviousMigrationId, MigrationsSqlGenerationOptions.NoTransactions);
        var upOne = migrator.GenerateScript(
            PreviousMigrationId, ShopMigrationId, MigrationsSqlGenerationOptions.NoTransactions);

        // Hạ về migration trước rồi chèn dòng cấu hình cũ để mô phỏng DB đang chạy trước khi nâng cấp.
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync(downOne);
            await context.Database.ExecuteSqlRawAsync("""
                DELETE FROM commerce_settings;
                INSERT INTO commerce_settings
                    ("id", "freeShippingThreshold", "codMaxAmount", "maxQuantityPerSku", "maxCartLines",
                     "sepayAccountNumber", "sepayBankCode", "updatedAt")
                VALUES
                    ('singleton', 600000, 15000000, 4, 25, '0123456789', 'MB', TIMESTAMP '2026-11-01 00:00:00');
                """);

            await context.Database.ExecuteSqlRawAsync(upOne);

            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                SELECT "freeShippingThreshold", "codMaxAmount", "maxQuantityPerSku", "maxCartLines",
                       "sepayAccountNumber", "sepayBankCode",
                       "shopName", "shopAddress", "shopPhone", "shopTaxCode"
                FROM commerce_settings
                WHERE "id" = 'singleton'
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(600_000, reader.GetInt32(0));
            Assert.Equal(15_000_000, reader.GetInt32(1));
            Assert.Equal(4, reader.GetInt32(2));
            Assert.Equal(25, reader.GetInt32(3));
            Assert.Equal("0123456789", reader.GetString(4));
            Assert.Equal("MB", reader.GetString(5));
            Assert.True(reader.IsDBNull(6));
            Assert.True(reader.IsDBNull(7));
            Assert.True(reader.IsDBNull(8));
            Assert.True(reader.IsDBNull(9));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
