using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Persistence;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// POOL-01: `DATABASE_POOL_MAX` phải áp cho chuỗi kết nối của mọi module, vì Supabase Session pooler chỉ cho 15 kết nối.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class DatabasePoolLimitTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>
{
    private static int MaxPoolSizeOf(System.Data.Common.DbConnection connection) =>
        new NpgsqlConnectionStringBuilder(connection.ConnectionString).MaxPoolSize;

    [Fact]
    public void EveryModuleDbContext_UsesTheConfiguredPoolLimit()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("DATABASE_POOL_MAX", "7"));
        using var scope = limited.Services.CreateScope();
        var provider = scope.ServiceProvider;

        Assert.Equal(7, MaxPoolSizeOf(provider.GetRequiredService<IdentityDbContext>().Database.GetDbConnection()));
        Assert.Equal(7, MaxPoolSizeOf(provider.GetRequiredService<CatalogDbContext>().Database.GetDbConnection()));
        Assert.Equal(7, MaxPoolSizeOf(provider.GetRequiredService<AiGatewayDbContext>().Database.GetDbConnection()));
        Assert.Equal(7, MaxPoolSizeOf(provider.GetRequiredService<ShippingDbContext>().Database.GetDbConnection()));
    }

    [Fact]
    public void WithoutTheVariable_KeepsTheNpgsqlDefault()
    {
        using var scope = factory.Services.CreateScope();

        var connection = scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.GetDbConnection();

        Assert.Equal(new NpgsqlConnectionStringBuilder().MaxPoolSize, MaxPoolSizeOf(connection));
    }

    [Fact]
    // AI gateway tạo `DbContext` qua factory cho worker nền. Worker cũng phải chịu giới hạn, vì worker giữ kết nối.
    public void AiGatewayContextFactory_UsesTheConfiguredPoolLimit()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("DATABASE_POOL_MAX", "7"));
        var contextFactory = limited.Services.GetRequiredService<IDbContextFactory<AiGatewayDbContext>>();

        using var context = contextFactory.CreateDbContext();

        Assert.Equal(7, MaxPoolSizeOf(context.Database.GetDbConnection()));
    }
}
