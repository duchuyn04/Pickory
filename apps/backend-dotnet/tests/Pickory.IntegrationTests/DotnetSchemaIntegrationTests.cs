using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Persistence;
using Xunit;

namespace Pickory.IntegrationTests;

public sealed class DotnetSchemaIntegrationTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    [Fact]
    public async Task ModuleModels_MatchAllTwentyPrismaTablesAndActualPostgresSchema()
    {
        // DI phải cung cấp context thật để phát hiện cấu hình đăng ký khác với cấu hình module.
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var aiGateway = scope.ServiceProvider.GetRequiredService<AiGatewayDbContext>();
        AssertTestConnection(identity);
        AssertTestConnection(catalog);
        AssertTestConnection(aiGateway);
        await PrismaDatabaseSchema.RequireMigratedSchemaAsync(identity);

        // Mỗi module chỉ sở hữu các bảng được chỉ định, tránh quan hệ kéo bảng module khác vào model.
        AssertModuleTables(identity, PrismaDatabaseSchema.IdentityTables);
        AssertModuleTables(catalog, PrismaDatabaseSchema.CatalogTables);
        AssertModuleTables(aiGateway, PrismaDatabaseSchema.AiGatewayTables);
        var expected = PrismaDatabaseSchema.ReadModel([identity, catalog, aiGateway]);
        Assert.Equal(20, expected.Keys.Count(key => key.StartsWith("table:", StringComparison.Ordinal)));

        // Nhãn enum phải khớp migration Prisma, kể cả khi model và DB cùng có nhãn ngoài hợp đồng.
        var actual = await PrismaDatabaseSchema.ReadAsync(identity);
        var enumEntries = actual.Where(entry => entry.Key.StartsWith("enum:", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        PrismaDatabaseSchema.AssertEqual(new Dictionary<string, string>
        {
            ["enum:public.UserKind"] = "customer,staff",
            ["enum:public.EmailTokenPurpose"] = "verify,reset,invite",
            ["enum:public.ProductStatus"] = "draft,active,hidden,discontinued",
            ["enum:public.InventoryMovementType"] = "manual,order_reserve,order_release,return"
        }, enumEntries);

        // So sánh toàn bộ schema `public` để các bảng hoặc chỉ mục không biết trước cũng gây lỗi.
        PrismaDatabaseSchema.AssertEqual(expected, actual);
    }

    internal static void AssertTestConnection(DbContext context)
    {
        // Chỉ resolver hiện có được phép chọn DB test; không sử dụng `DATABASE_URL` làm dự phòng.
        var expectedConnection = new NpgsqlConnectionStringBuilder(
            PostgresConnectionString.Convert(PickoryWebApplicationFactory.ResolveTestDatabaseUrl()));
        var actualConnection = new NpgsqlConnectionStringBuilder(context.Database.GetDbConnection().ConnectionString);
        Assert.True(expectedConnection.Host == actualConnection.Host &&
                    expectedConnection.Port == actualConnection.Port &&
                    expectedConnection.Database == actualConnection.Database &&
                    expectedConnection.Username == actualConnection.Username,
            $"{context.GetType().Name} must use the database selected by TEST_DATABASE_URL, not the development database.");
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    private static void AssertModuleTables(DbContext context, IEnumerable<string> expectedTables)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        Assert.Equal(expectedTables.Order(StringComparer.Ordinal),
            model.GetRelationalModel().Tables.Select(table => table.Name).Order(StringComparer.Ordinal));
        Assert.All(model.GetRelationalModel().Tables, table =>
            Assert.True(table.Schema is null or "public", $"Application table {table.Name} must use public, not {table.Schema}."));
    }
}
