using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Shared.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class IntegrationDatabaseSafetyTests(DatabaseFixture database)
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
        var competingSuite = new DatabaseFixture();
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

    [Fact]
    public void TwoCollectionFixtures_HaveDifferentDatabaseNames()
    {
        // Khởi tạo hai fixture đại diện cho hai module khác nhau.
        var identityFixture = new IdentityDatabaseFixture();
        var catalogFixture = new CatalogDatabaseFixture();

        // Kiểm tra hậu tố của hai fixture phải khác nhau để không ghi đè DB của nhau.
        Assert.NotEqual(identityFixture.DatabaseSuffix, catalogFixture.DatabaseSuffix);

        // Sinh chuỗi URL cơ sở dữ liệu cho từng fixture từ cùng một URL kiểm thử gốc.
        var identityUrl = IntegrationDatabaseFixture.BuildDatabaseUrl("postgresql://tester:secret@127.0.0.1:5432/pickory_test", identityFixture.DatabaseSuffix);
        var catalogUrl = IntegrationDatabaseFixture.BuildDatabaseUrl("postgresql://tester:secret@127.0.0.1:5432/pickory_test", catalogFixture.DatabaseSuffix);

        // Chuyển đổi URL sang connection string builder để so sánh tên cơ sở dữ liệu.
        var identityBuilder = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(identityUrl));
        var catalogBuilder = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(catalogUrl));

        // Tên cơ sở dữ liệu của hai module bắt buộc phải khác nhau.
        Assert.NotEqual(identityBuilder.Database, catalogBuilder.Database);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("catalog")]
    [InlineData("shipping")]
    [InlineData("support_desk")]
    public async Task SuffixedDatabase_PreservesTestComponentAndPassesSafetyValidation(string suffix)
    {
        // Ghép hậu tố vào URL kiểm thử ban đầu để tạo URL mới.
        var suffixedUrl = IntegrationDatabaseFixture.BuildDatabaseUrl("postgresql://tester:secret@127.0.0.1:5432/pickory_test", suffix);
        var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(suffixedUrl));

        // Tên cơ sở dữ liệu mới bắt buộc phải chứa thành phần test để bảo đảm an toàn.
        Assert.Contains("test", builder.Database!.Split(['_', '-']));

        // Kiểm tra xác thực an toàn không báo lỗi khi DB phát triển khác tên.
        await IntegrationDatabaseFixture.ValidateTestTargetAsync(builder, "postgresql://tester:secret@127.0.0.1:5432/pickory");
    }

    [Fact]
    public async Task EnsureDatabaseCreatedAsync_CreatesDatabaseWhenMissing()
    {
        // Tạo tên DB ngẫu nhiên phục vụ kiểm thử tạo DB chưa tồn tại.
        var testDbName = $"pickory_test_create_probe_{Guid.NewGuid().ToString("N")[..8]}";
        var baseBuilder = new NpgsqlConnectionStringBuilder(database.ConnectionString);
        var probeBuilder = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = testDbName,
            Pooling = false
        };

        // Xóa trước DB thử nghiệm nếu đã tồn tại từ lần chạy trước.
        await IntegrationDatabaseFixture.DropDatabaseIfExistsAsync(baseBuilder, testDbName);

        try
        {
            // Thực hiện tạo DB mới bằng hàm chuyên dụng.
            await IntegrationDatabaseFixture.EnsureDatabaseCreatedAsync(probeBuilder);

            // Kiểm tra DB vừa tạo đã thực sự xuất hiện trong hệ thống.
            var exists = await IntegrationDatabaseFixture.DatabaseExistsAsync(baseBuilder, testDbName);
            Assert.True(exists);
        }
        finally
        {
            // Dọn dẹp DB thử nghiệm sau khi kiểm tra xong.
            await IntegrationDatabaseFixture.DropDatabaseIfExistsAsync(baseBuilder, testDbName);
        }
    }

    [Fact]
    public async Task ValidateTestTargetAsync_RejectsTargetMatchingDevelopmentDatabaseUrl()
    {
        // Dựng connection string builder trỏ tới DB có hậu tố kiểm thử.
        var target = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert("postgresql://tester:secret@127.0.0.1:5432/pickory_test_identity"));

        // Từ chối thực thi khi URL phát triển trùng hoàn toàn với DB kiểm thử.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IntegrationDatabaseFixture.ValidateTestTargetAsync(target, "postgresql://tester:secret@127.0.0.1:5432/pickory_test_identity"));
        Assert.Contains("development database", error.Message);
    }

    [Fact]
    public async Task SameDatabase_CannotHoldTwoAdvisoryLocks()
    {
        // Mở kết nối thăm dò tới DB của collection hiện tại đang nắm khóa phiên.
        await using var probe = new NpgsqlConnection(database.ConnectionString);
        await probe.OpenAsync();

        // Thử chiếm cùng khóa advisory trên kết nối thứ hai.
        await using var lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", probe);
        lockCommand.Parameters.AddWithValue("key", IntegrationDatabaseFixture.AdvisoryLockKey);

        // Khóa đang bận nên kết quả trả về bắt buộc phải là false.
        var acquired = await lockCommand.ExecuteScalarAsync();
        Assert.Equal(false, acquired);
    }

    [Fact]
    public async Task DifferentDatabases_DoNotBlockEachOthersAdvisoryLock()
    {
        // Mở kết nối tới DB postgres hệ thống thay vì DB kiểm thử của fixture.
        var otherBuilder = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var otherConn = new NpgsqlConnection(otherBuilder.ConnectionString);
        await otherConn.OpenAsync();

        // Chiếm khóa advisory cùng mã khóa trên cơ sở dữ liệu khác.
        await using var lockCmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", otherConn);
        lockCmd.Parameters.AddWithValue("key", IntegrationDatabaseFixture.AdvisoryLockKey);
        var acquired = await lockCmd.ExecuteScalarAsync();

        // Giải phóng khóa vừa chiếm trên DB hệ thống để không để lại trạng thái rác.
        await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", otherConn);
        unlockCmd.Parameters.AddWithValue("key", IntegrationDatabaseFixture.AdvisoryLockKey);
        await unlockCmd.ExecuteNonQueryAsync();

        // Advisory lock trong PostgreSQL là độc lập theo từng DB nên phải thành công.
        Assert.Equal(true, acquired);
    }

    [Fact]
    public async Task FailedFixture_DoesNotLeaveAdvisoryLockOrphaned()
    {
        // Tạo một DB tạm thời để thử nghiệm lỗi trong quá trình khởi tạo fixture.
        var baseBuilder = new NpgsqlConnectionStringBuilder(database.ConnectionString);
        var probeSuffix = $"fail_{Guid.NewGuid().ToString("N")[..6]}";
        var probeDbName = $"pickory_test_{probeSuffix}";
        var probeTarget = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = probeDbName,
            Pooling = false
        };
        await IntegrationDatabaseFixture.EnsureDatabaseCreatedAsync(probeTarget);

        try
        {
            var probeFixture = new IntegrationDatabaseFixture(probeSuffix);
            // Giữ một kết nối ngoài để bước kiểm tra RefuseOtherClientsAsync gây lỗi.
            await using (var blocker = new NpgsqlConnection(probeTarget.ConnectionString))
            {
                await blocker.OpenAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(() => probeFixture.InitializeAsync());
            }

            // Sau khi fixture gặp sự cố, khóa phải được tự động giải phóng hoàn toàn.
            await using var verifyConn = new NpgsqlConnection(probeTarget.ConnectionString);
            await verifyConn.OpenAsync();
            await using var lockCmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", verifyConn);
            lockCmd.Parameters.AddWithValue("key", IntegrationDatabaseFixture.AdvisoryLockKey);
            var acquired = await lockCmd.ExecuteScalarAsync();

            // Mở khóa vừa thử thành công để dọn dẹp sạch sẽ.
            await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", verifyConn);
            unlockCmd.Parameters.AddWithValue("key", IntegrationDatabaseFixture.AdvisoryLockKey);
            await unlockCmd.ExecuteNonQueryAsync();

            Assert.Equal(true, acquired);
        }
        finally
        {
            await IntegrationDatabaseFixture.DropDatabaseIfExistsAsync(baseBuilder, probeDbName);
        }
    }

    private sealed class ProbeDatabaseFixture(string suffix) : IntegrationDatabaseFixture(suffix);

    [Fact]
    public async Task DisposeAsync_ClearsPooledConnectionsUsedByFactory()
    {
        var probeSuffix = $"pool_{Guid.NewGuid().ToString("N")[..6]}";
        var probeDbName = $"pickory_test_{probeSuffix}";
        var baseBuilder = new NpgsqlConnectionStringBuilder(database.ConnectionString);
        var probeFixture = new ProbeDatabaseFixture(probeSuffix);
        await probeFixture.InitializeAsync();

        // Mở kết nối qua chuỗi pooled của probeFixture để nạp kết nối vào pool.
        var probePooledConnectionString = PostgresConnectionString.Convert(probeFixture.DatabaseUrl);
        int probePidBefore;
        await using (var connection = new NpgsqlConnection(probePooledConnectionString))
        {
            await connection.OpenAsync();
            probePidBefore = connection.ProcessID;
        }

        // Gọi DisposeAsync của fixture để dọn pool kết nối.
        await probeFixture.DisposeAsync();

        try
        {
            // Mở lại kết nối sau khi fixture đã dispose và xác nhận PID đã đổi.
            await using var reopened = new NpgsqlConnection(probePooledConnectionString);
            await reopened.OpenAsync();
            Assert.NotEqual(probePidBefore, reopened.ProcessID);
        }
        finally
        {
            // Dọn dẹp pool và xóa DB tạm sau khi kiểm tra xong.
            IntegrationDatabaseFixture.ClearSpecificPool(probePooledConnectionString);
            await IntegrationDatabaseFixture.DropDatabaseIfExistsAsync(baseBuilder, probeDbName);
        }
    }

    [Fact]
    public async Task ParallelCollections_DisposingOneDoesNotClearPoolOfAnother()
    {
        // Tạo hai connection string có pooling trỏ tới hai cơ sở dữ liệu riêng biệt.
        var builder1 = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Pooling = true,
            ApplicationName = "pool-test-1"
        };
        var builder2 = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = "postgres",
            Pooling = true,
            ApplicationName = "pool-test-2"
        };

        int pid2Before;
        // Mở và đóng kết nối để nạp sẵn connection vào từng pool.
        await using (var conn1 = new NpgsqlConnection(builder1.ConnectionString))
        {
            await conn1.OpenAsync();
        }
        await using (var conn2 = new NpgsqlConnection(builder2.ConnectionString))
        {
            await conn2.OpenAsync();
            pid2Before = conn2.ProcessID;
        }

        try
        {
            // Chỉ xóa pool của chuỗi kết nối thứ nhất.
            IntegrationDatabaseFixture.ClearSpecificPool(builder1.ConnectionString);

            // Mở lại kết nối của chuỗi thứ hai để xác nhận pool không bị ảnh hưởng.
            await using var conn2Reopened = new NpgsqlConnection(builder2.ConnectionString);
            await conn2Reopened.OpenAsync();
            Assert.Equal(pid2Before, conn2Reopened.ProcessID);
        }
        finally
        {
            // Dọn dẹp pool thứ hai sau khi kiểm tra xong.
            IntegrationDatabaseFixture.ClearSpecificPool(builder2.ConnectionString);
        }
    }
}
