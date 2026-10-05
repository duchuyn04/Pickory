using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Shared.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class IntegrationDatabaseSafetyTests(IntegrationDatabaseFixture database)
{
    [Theory]
    [InlineData("postgresql://tester:secret@127.0.0.1/pickory_test", "postgresql://developer:different@127.0.0.1/pickory_test")]
    [InlineData("postgresql://tester:secret@127.0.0.1/pickory_test", "postgresql://developer:different@localhost/pickory_test")]
    public async Task ResetGuard_RejectsDevelopmentDatabaseEvenWithDifferentCredentialsOrHostAlias(string testUrl, string developmentUrl)
    {
        // Đích DB không thay đổi khi người dùng hoặc tên máy khác nhau.
        var test = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(testUrl));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IntegrationDatabaseFixture.ValidateTestTargetAsync(test, developmentUrl));
        Assert.Contains("development database", error.Message);
    }

    [Theory]
    [InlineData("pickory")]
    [InlineData("latest")]
    [InlineData("postgres")]
    public async Task ResetGuard_RejectsDatabaseNamesWithoutDedicatedTestComponent(string databaseName)
    {
        // Chuỗi con như `latest` không xác định DB kiểm thử chuyên dụng.
        var test = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Database = databaseName };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IntegrationDatabaseFixture.ValidateTestTargetAsync(test, null));
        Assert.Contains("dedicated database", error.Message);
    }

    [Fact]
    public async Task ResetGuard_RefusesConnectedClientsInsteadOfTerminatingThemOrResettingSchema()
    {
        // Kết nối ngoài phiên khóa phải làm điều kiện reset thất bại, kể cả khi phiên nhàn rỗi.
        await using var context = database.CreateIdentityContext();
        var before = await ApplicationDatabaseSchema.ReadAsync(context);
        await using var probe = new NpgsqlConnection(database.ConnectionString);
        await probe.OpenAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IntegrationDatabaseFixture.RefuseOtherClientsAsync(probe));
        Assert.Contains("other clients are connected", error.Message);
        Assert.Contains("pid=", error.Message);
        ApplicationDatabaseSchema.AssertEqual(before, await ApplicationDatabaseSchema.ReadAsync(context));
    }

    [Fact]
    public async Task CompetingSuite_CannotAcquireSessionLockOrResetSchemaWhileCollectionIsActive()
    {
        // Khóa phải từ chối lần khởi tạo thứ hai trước mọi lệnh reset.
        await using var context = database.CreateIdentityContext();
        var before = await ApplicationDatabaseSchema.ReadAsync(context);
        var histories = await BaselineMigrationIntegrationTests.ReadHistoryAsync(context);
        var competingSuite = new IntegrationDatabaseFixture();
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => competingSuite.InitializeAsync());
            Assert.Contains("already locked", error.Message);
            ApplicationDatabaseSchema.AssertEqual(before, await ApplicationDatabaseSchema.ReadAsync(context));
            BaselineMigrationIntegrationTests.AssertHistoriesEqual(histories,
                await BaselineMigrationIntegrationTests.ReadHistoryAsync(context));
        }
        finally
        {
            // Đóng phiên cạnh tranh kể cả khi assertion lỗi.
            await competingSuite.DisposeAsync();
        }
    }
}
