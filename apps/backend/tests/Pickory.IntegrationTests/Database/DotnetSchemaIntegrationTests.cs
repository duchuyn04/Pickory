using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.ActivityTracking.Persistence;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Shared.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class DotnetSchemaIntegrationTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    [Fact]
    public async Task EfMigrations_CreateAllTablesMatchingReferenceSchemaAndModuleModels()
    {
        // Đăng ký dịch vụ cung cấp context thật để phát hiện cấu hình khác cấu hình module.
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var commerce = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        var activityTracking = scope.ServiceProvider.GetRequiredService<ActivityTrackingDbContext>();
        AssertTestConnection(identity);
        AssertTestConnection(catalog);
        AssertTestConnection(shipping);
        AssertTestConnection(commerce);
        AssertTestConnection(activityTracking);

        // Mỗi module chỉ được sở hữu bảng đã chỉ định, không kéo quan hệ module khác vào model.
        AssertModuleTables(identity, ApplicationDatabaseSchema.IdentityTables);
        AssertModuleTables(catalog, ApplicationDatabaseSchema.CatalogTables);
        AssertModuleTables(shipping, ApplicationDatabaseSchema.ShippingTables);
        AssertModuleTables(commerce, ApplicationDatabaseSchema.CommerceTables);
        AssertModuleTables(activityTracking, ApplicationDatabaseSchema.ActivityTrackingTables);
        var model = ApplicationDatabaseSchema.ReadModel([identity, catalog, shipping, commerce, activityTracking]);
        // Mỗi module sở hữu số bảng cố định: identity 9, catalog 8, shipping 7, commerce 10, activity-tracking 1. Ba bảng AI đã gỡ nên tổng là 35.
        Assert.Equal(35, model.Keys.Count(key => key.StartsWith("table:", StringComparison.Ordinal)));

        // Metadata thực phải khớp dump, kể cả thứ tự cột, cập nhật khóa ngoại và mô tả extension.
        var actual = await ApplicationDatabaseSchema.ReadAsync(identity);
        var reference = await DatabaseSqlFiles.ReadReferenceAsync(identity);
        // Bản chụp Prisma giữ index cũ; so với schema baseline thật thay vì sửa bản tham chiếu.
        await using (var baselineTransaction = await identity.Database.BeginTransactionAsync())
        {
            var migrator = catalog.GetService<IMigrator>();
            var latest = catalog.GetService<IMigrationsAssembly>().Migrations.Keys.Order(StringComparer.Ordinal).Last();
            // Hạ riêng Catalog trong transaction để vẫn kiểm đầy đủ metadata gốc của Prisma.
            await identity.Database.ExecuteSqlRawAsync(migrator.GenerateScript(
                latest, "20261005125646_PrismaBaseline", MigrationsSqlGenerationOptions.NoTransactions));
            var baseline = await ApplicationDatabaseSchema.ReadAsync(identity);
            var baselineWithoutNewModules = baseline.Where(entry => !ApplicationDatabaseSchema.IsShippingObject(entry.Key) &&
                !ApplicationDatabaseSchema.IsCommerceObject(entry.Key) &&
                !ApplicationDatabaseSchema.IsActivityTrackingObject(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            ApplicationDatabaseSchema.AssertEqual(reference, baselineWithoutNewModules);
            // Rollback khôi phục index mới trước khi so model hiện tại với DB.
            await baselineTransaction.RollbackAsync();
        }

        // Phép so model với DB thật xét cả shipping, commerce và activity-tracking, nên đây là nơi kiểm schema của ba module đó.
        // Metadata Entity Framework Core không biểu diễn thứ tự cột vật lý hoặc mô tả extension.
        var modeledActual = actual.Where(entry => !entry.Key.StartsWith("ordinal:", StringComparison.Ordinal) &&
                                                   !entry.Key.StartsWith("extension:", StringComparison.Ordinal) &&
                                                   !entry.Key.StartsWith("comment:", StringComparison.Ordinal) &&
                                                   !entry.Key.StartsWith("definition:", StringComparison.Ordinal) &&
                                                   !entry.Key.StartsWith("schema:", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        // Entity Framework Core không biểu diễn `ON UPDATE`. So sánh dump đã kiểm tra riêng hành vi thực này.
        foreach (var key in modeledActual.Keys.Where(key => key.StartsWith("foreign:", StringComparison.Ordinal)).ToArray())
        {
            var signature = modeledActual[key];
            modeledActual[key] = signature[..signature.LastIndexOf(";update=", StringComparison.Ordinal)];
        }
        ApplicationDatabaseSchema.AssertEqual(model, modeledActual);

        // Entity Framework Core không cần tạo lịch sử Node trên DB mới.
        Assert.False(await identity.Database.SqlQueryRaw<bool>("SELECT to_regclass('public._prisma_migrations') IS NOT NULL AS \"Value\"").SingleAsync());
    }

    internal static void AssertTestConnection(DbContext context)
    {
        // Không cho phép context dự phòng bằng DB phát triển.
        var expected = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(PickoryWebApplicationFactory.ResolveTestDatabaseUrl()));
        var actual = new NpgsqlConnectionStringBuilder(context.Database.GetDbConnection().ConnectionString);
        Assert.True(expected.Host == actual.Host && expected.Port == actual.Port &&
                    expected.Database == actual.Database && expected.Username == actual.Username,
            $"{context.GetType().Name} must use TEST_DATABASE_URL, not the development database.");
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    private static void AssertModuleTables(DbContext context, IEnumerable<string> expectedTables)
    {
        // Model thiết kế giữ đầy đủ bảng ánh xạ thay vì model thực thi rút gọn.
        var model = context.GetService<IDesignTimeModel>().Model;
        Assert.Equal(expectedTables.Order(StringComparer.Ordinal),
            model.GetRelationalModel().Tables.Select(table => table.Name).Order(StringComparer.Ordinal));
        Assert.All(model.GetRelationalModel().Tables, table =>
            Assert.True(table.Schema is null or "public", $"Application table {table.Name} must use public, not {table.Schema}."));
    }
}
