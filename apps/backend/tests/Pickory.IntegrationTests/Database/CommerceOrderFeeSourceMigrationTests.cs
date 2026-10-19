using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database;

// COM-27: migration thêm cột `shippingFeeSource` vào bảng `orders`. Đơn đặt trước migration phải nhận nguồn `ghn`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CommerceOrderFeeSourceMigrationTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private const string FeeSourceMigrationId = "20261019144909_AddOrderShippingFeeSource";

    [Fact]
    public async Task Migration_OrderInsertedBeforeTheColumnExistsReceivesGhnSource()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        var migrationIds = context.GetService<IMigrationsAssembly>().Migrations.Keys.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(FeeSourceMigrationId, migrationIds[^1]);
        var previousMigrationId = migrationIds[^2];
        var migrator = context.GetService<IMigrator>();

        // Rollback giữ nguyên DB test. Mọi thay đổi schema và dòng đơn tạm chỉ tồn tại trong transaction này.
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            // Đưa schema về trạng thái trước migration, để chèn được một đơn như đơn đặt trước khi có cột mới.
            await context.Database.ExecuteSqlRawAsync(migrator.GenerateScript(
                FeeSourceMigrationId, previousMigrationId, MigrationsSqlGenerationOptions.NoTransactions));
            Assert.Equal(0, await CountColumnAsync(context));

            // `jsonb_build_object()` tạo đối tượng rỗng mà không cần dấu ngoặc nhọn, vì lệnh SQL thô hiểu `{}` là chỗ điền tham số.
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO orders
                    (id, code, "anonymousId", contact, "shippingAddress", "paymentMethod", status, "paymentStatus",
                     subtotal, "shippingFee", "shippingDiscount", total, "accessTokenHash", "idempotencyKey")
                VALUES
                    ('pre-migration-order', 'PKPRE0001', 'anon-pre', jsonb_build_object(), jsonb_build_object(),
                     'cod', 'pending_confirmation', 'unpaid', 100000, 30000, 0, 130000, 'hash', 'key-pre-migration')
                """);

            // Áp lại migration mới. Cột `NOT NULL` chỉ thêm được vào bảng đã có dòng khi cột có giá trị mặc định.
            await context.Database.ExecuteSqlRawAsync(migrator.GenerateScript(
                previousMigrationId, FeeSourceMigrationId, MigrationsSqlGenerationOptions.NoTransactions));

            Assert.Equal(1, await CountColumnAsync(context));
            var source = await context.Database
                .SqlQueryRaw<string>("""
                    SELECT "shippingFeeSource"::text AS "Value" FROM orders WHERE id = 'pre-migration-order'
                    """)
                .SingleAsync();
            Assert.Equal("ghn", source);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    // Đếm cột `shippingFeeSource` trong bảng `orders`, để biết schema đang ở trước hay sau migration.
    private static Task<int> CountColumnAsync(CommerceDbContext context) =>
        context.Database
            .SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'orders' AND column_name = 'shippingFeeSource'
                """)
            .SingleAsync();
}
