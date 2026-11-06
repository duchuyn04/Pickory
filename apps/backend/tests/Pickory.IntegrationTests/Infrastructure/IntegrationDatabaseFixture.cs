using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Modules.ActivityTracking.Persistence;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Shared.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Infrastructure;


public class IntegrationDatabaseFixture : IAsyncLifetime
{
    // Khóa advisory dùng chung để ngăn hai tiến trình chạy đè lên cùng một DB.
    public const long AdvisoryLockKey = 578524097137070905;
    private NpgsqlConnection? _lockConnection;
    // Hậu tố gắn vào tên DB để tách biệt dữ liệu giữa các collection song song.
    public string DatabaseSuffix { get; }
    public string DatabaseUrl { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;
    public bool Initialized { get; private set; }

    protected internal IntegrationDatabaseFixture(string databaseSuffix)
    {
        // Yêu cầu hậu tố rõ ràng để mỗi collection sử dụng đúng cơ sở dữ liệu riêng.
        DatabaseSuffix = databaseSuffix;
    }
    public static string BuildDatabaseUrl(string baseDatabaseUrl, string suffix)
    {
        // Trả về URL gốc khi không có hậu tố cần bổ sung.
        var trimmedSuffix = suffix.Trim().TrimStart('_');
        if (string.IsNullOrEmpty(trimmedSuffix))
        {
            return baseDatabaseUrl;
        }

        // Tách đường dẫn DB từ URI gốc và ghép hậu tố module.
        var uriBuilder = new UriBuilder(baseDatabaseUrl);
        var baseDb = Uri.UnescapeDataString(uriBuilder.Path.TrimStart('/'));

        // Giữ nguyên tên nếu DB đã có sẵn hậu tố để tránh nhân đôi hậu tố.
        var targetDb = baseDb.EndsWith($"_{trimmedSuffix}", StringComparison.OrdinalIgnoreCase)
            ? baseDb
            : $"{baseDb}_{trimmedSuffix}";

        uriBuilder.Path = "/" + Uri.EscapeDataString(targetDb);
        return uriBuilder.Uri.AbsoluteUri;
    }

    internal static async Task EnsureDatabaseCreatedAsync(NpgsqlConnectionStringBuilder target)
    {
        var databaseName = target.Database;
        // Bắt buộc phải có tên DB để không tạo kết nối rỗng.
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Mục tiêu kết nối phải có tên DB.");
        }

        // Kết nối maintenance DB postgres để kiểm tra sự tồn tại của DB đích.
        var maintenanceBuilder = new NpgsqlConnectionStringBuilder(target.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };

        await using var connection = new NpgsqlConnection(maintenanceBuilder.ConnectionString);
        await connection.OpenAsync();

        // Kiểm tra xem DB đích đã được tạo trong catalog PostgreSQL hay chưa.
        await using var existsCommand = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        existsCommand.Parameters.AddWithValue("name", databaseName);
        if (await existsCommand.ExecuteScalarAsync() is not null)
        {
            return;
        }

        // Bao ngoặc kép tên DB để tránh lỗi cú pháp SQL với ký tự đặc biệt.
        var quotedName = databaseName.Replace("\"", "\"\"");
        try
        {
            await using var createCommand = new NpgsqlCommand($"CREATE DATABASE \"{quotedName}\"", connection);
            await createCommand.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P04")
        {
            // Bỏ qua lỗi nếu collection song song khác đã kịp tạo DB trước.
        }
    }

    internal static async Task DropDatabaseIfExistsAsync(NpgsqlConnectionStringBuilder baseBuilder, string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        // Giới hạn phạm vi xóa vào các DB kiểm thử tạm thời để bảo vệ dữ liệu khác.
        if (!databaseName.StartsWith("pickory_test_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chỉ cho phép xóa các DB thử nghiệm có tiền tố pickory_test_.");
        }

        // Kết nối maintenance DB postgres để thực hiện thao tác xóa DB.
        var maintenanceBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };

        await using var connection = new NpgsqlConnection(maintenanceBuilder.ConnectionString);
        await connection.OpenAsync();

        // Ngắt toàn bộ kết nối khác đang mở tới DB đích trước khi xóa.
        await using var terminateCommand = new NpgsqlCommand("""
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = @name AND pid <> pg_backend_pid()
            """, connection);
        terminateCommand.Parameters.AddWithValue("name", databaseName);
        await terminateCommand.ExecuteNonQueryAsync();

        // Xóa cơ sở dữ liệu nếu tồn tại sau khi đã ngắt kết nối.
        var quotedName = databaseName.Replace("\"", "\"\"");
        await using var dropCommand = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{quotedName}\"", connection);
        await dropCommand.ExecuteNonQueryAsync();
    }

    internal static async Task<bool> DatabaseExistsAsync(NpgsqlConnectionStringBuilder baseBuilder, string databaseName)
    {
        // Kết nối maintenance DB postgres để tra cứu danh sách DB hệ thống.
        var maintenanceBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };

        await using var connection = new NpgsqlConnection(maintenanceBuilder.ConnectionString);
        await connection.OpenAsync();

        // Tra cứu catalog pg_database để kiểm tra DB có tồn tại không.
        await using var existsCommand = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        existsCommand.Parameters.AddWithValue("name", databaseName);
        return await existsCommand.ExecuteScalarAsync() is not null;
    }

    internal static void ClearSpecificPool(string connectionString)
    {
        // Chỉ dọn dẹp pool kết nối gắn với đúng chuỗi kết nối này.
        using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    public async Task InitializeAsync()
    {
        // Xác định URL cơ sở dữ liệu có gắn hậu tố của collection.
        var rawTestUrl = PickoryWebApplicationFactory.ResolveTestDatabaseUrl();
        DatabaseUrl = BuildDatabaseUrl(rawTestUrl, DatabaseSuffix);
        var connection = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(DatabaseUrl));

        // Kiểm tra an toàn để từ chối DB phát triển trước mọi kết nối schema.
        await ValidateTestTargetAsync(connection, PickoryWebApplicationFactory.ResolveEnvironmentValue("DATABASE_URL"));

        // Tạo cơ sở dữ liệu khi chưa tồn tại trước khi mở kết nối khóa phiên.
        await EnsureDatabaseCreatedAsync(connection);

        connection.Pooling = false;
        connection.ApplicationName = $"pickory-dotnet-tests-{Guid.NewGuid():N}";
        ConnectionString = connection.ConnectionString;
        _lockConnection = new NpgsqlConnection(ConnectionString);
        try
        {
            // Khóa phiên tồn tại suốt collection để tiến trình kiểm thử khác không thể reset DB.
            await _lockConnection.OpenAsync();
            await using var lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", _lockConnection);
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            if (await lockCommand.ExecuteScalarAsync() is not true)
            {
                throw new InvalidOperationException("TEST_DATABASE_URL is already locked by another integration suite. Stop that suite before retrying.");
            }

            // Phiên ngoài collection có thể giữ dữ liệu hoặc khóa. Không ngắt phiên để ép reset.
            await RefuseOtherClientsAsync(_lockConnection);

            // Chỉ reset các schema thuộc ứng dụng; schema lạ cần người vận hành kiểm tra trước.
            await using var schemasCommand = new NpgsqlCommand("""
                SELECT nspname FROM pg_namespace
                WHERE nspname NOT LIKE 'pg_%' AND nspname NOT IN
                  ('information_schema', 'public', 'ef_identity', 'ef_ai_gateway', 'ef_catalog', 'ef_shipping', 'ef_commerce', 'ef_activity_tracking', 'ef_support_desk')
                ORDER BY nspname
                """, _lockConnection);
            var unexpectedSchemas = new List<string>();
            await using (var reader = await schemasCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    unexpectedSchemas.Add(reader.GetString(0));
                }
            }
            if (unexpectedSchemas.Count != 0)
            {
                throw new InvalidOperationException("Refusing to reset unexpected TEST_DATABASE_URL schemas: " + string.Join(", ", unexpectedSchemas));
            }

            // Giao dịch reset loại bỏ schema Node cũ trước khi Entity Framework Core tạo schema mới.
            await using (var transaction = await _lockConnection.BeginTransactionAsync())
            {
                await using var resetCommand = new NpgsqlCommand("""
                    SET LOCAL lock_timeout = '5s';
                    DROP SCHEMA IF EXISTS ef_identity CASCADE;
                    DROP SCHEMA IF EXISTS ef_ai_gateway CASCADE;
                    DROP SCHEMA IF EXISTS ef_catalog CASCADE;
                    DROP SCHEMA IF EXISTS ef_shipping CASCADE;
                    DROP SCHEMA IF EXISTS ef_commerce CASCADE;
                    DROP SCHEMA IF EXISTS ef_activity_tracking CASCADE;
                    DROP SCHEMA IF EXISTS ef_support_desk CASCADE;
                    DROP SCHEMA public CASCADE;
                    CREATE SCHEMA public AUTHORIZATION pg_database_owner;
                    COMMENT ON SCHEMA public IS 'standard public schema';
                    """, _lockConnection, transaction);
                await resetCommand.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }

            // Kiểm tra DB rỗng trước migration để không vô tình chứng minh đường nhận schema có sẵn.
            await using var emptyCommand = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public')
                     + (SELECT count(*) FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
                        WHERE n.nspname = 'public')
                """, _lockConnection);
            if (Convert.ToInt64(await emptyCommand.ExecuteScalarAsync()) != 0)
            {
                throw new InvalidOperationException("TEST_DATABASE_URL must have an empty application schema before EF migrations.");
            }

            // Cấu hình module thật bảo đảm Entity Framework Core tạo đủ schema cùng lịch sử riêng.
            await using var identity = CreateIdentityContext();
            await identity.Database.MigrateAsync();
            await using var catalog = CreateCatalogContext();
            await catalog.Database.MigrateAsync();
            await using var shipping = CreateShippingContext();
            await shipping.Database.MigrateAsync();
            await using var commerce = CreateCommerceContext();
            await commerce.Database.MigrateAsync();
            await using var activityTracking = CreateActivityTrackingContext();
            await activityTracking.Database.MigrateAsync();
            await using var supportDesk = CreateSupportDeskContext();
            await supportDesk.Database.MigrateAsync();
            Initialized = true;
        }
        catch
        {
            // Đóng phiên khi khởi tạo lỗi để khóa không chặn lần chạy tiếp theo.
            await _lockConnection.DisposeAsync();
            _lockConnection = null;
            throw;
        }
    }

    internal static async Task RefuseOtherClientsAsync(NpgsqlConnection connection)
    {
        // Tính cả phiên nhàn rỗi vì tiến trình khác có thể tiếp tục dùng DB ngay sau reset.
        await using var command = new NpgsqlCommand("""
            SELECT pid, COALESCE(application_name, ''), state
            FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid()
              AND backend_type = 'client backend'
            ORDER BY pid
            """, connection);
        var otherClients = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                otherClients.Add($"pid={reader.GetInt32(0)}, application={reader.GetString(1)}, state={(reader.IsDBNull(2) ? "unknown" : reader.GetString(2))}");
            }
        }
        // Không chứa URL hoặc mật khẩu trong lỗi từ chối reset.
        if (otherClients.Count != 0)
        {
            throw new InvalidOperationException("Refusing to reset TEST_DATABASE_URL while other clients are connected: " + string.Join("; ", otherClients));
        }
    }

    internal static async Task ValidateTestTargetAsync(NpgsqlConnectionStringBuilder test, string? developmentUrl)
    {
        // Tên DB phải có thành phần `test` rõ ràng để giới hạn quyền reset vào DB chuyên dụng.
        var nameParts = (test.Database ?? "").Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (!nameParts.Any(part => part.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                                   part.Equals("tests", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("TEST_DATABASE_URL must select a dedicated database whose name contains a test or tests component.");
        }
        if (string.IsNullOrWhiteSpace(developmentUrl))
        {
            return;
        }

        // Người dùng hoặc mật khẩu khác không biến cùng DB thành đích an toàn.
        var development = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(developmentUrl));
        if (test.Port != development.Port || !string.Equals(test.Database, development.Database, StringComparison.Ordinal))
        {
            return;
        }
        // Thiếu tên máy chủ phải dừng trước reset vì không thể xác nhận đích DB khác nhau.
        var testHost = test.Host
            ?? throw new InvalidOperationException("TEST_DATABASE_URL must specify a PostgreSQL host.");
        var developmentHost = development.Host
            ?? throw new InvalidOperationException("DATABASE_URL must specify a PostgreSQL host.");
        if (testHost.Equals(developmentHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to reset TEST_DATABASE_URL because it resolves to the DATABASE_URL development database.");
        }
        // Phân giải tên máy chủ chặn bí danh cùng trỏ tới DB phát triển.
        var testAddresses = await Dns.GetHostAddressesAsync(testHost);
        var developmentAddresses = await Dns.GetHostAddressesAsync(developmentHost);
        if (testAddresses.Select(address => address.MapToIPv6()).Intersect(developmentAddresses.Select(address => address.MapToIPv6())).Any())
        {
            throw new InvalidOperationException("Refusing to reset TEST_DATABASE_URL because it resolves to the DATABASE_URL development database.");
        }
    }

    public IdentityDbContext CreateIdentityContext()
    {
        // Dùng cùng tùy chọn module để enum và lịch sử không khác ứng dụng thật.
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.ConfigureOptions(options, ConnectionString);
        return new IdentityDbContext(options.Options);
    }

    public CatalogDbContext CreateCatalogContext()
    {
        // Dùng cùng tùy chọn module để enum và lịch sử không khác ứng dụng thật.
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.ConfigureOptions(options, ConnectionString);
        return new CatalogDbContext(options.Options);
    }

    public ShippingDbContext CreateShippingContext()
    {
        // Dùng cùng tùy chọn module để enum và lịch sử không khác ứng dụng thật.
        var options = new DbContextOptionsBuilder<ShippingDbContext>();
        ShippingDbContext.ConfigureOptions(options, ConnectionString);
        return new ShippingDbContext(options.Options);
    }

    public CommerceDbContext CreateCommerceContext()
    {
        // Dùng cùng tùy chọn module để enum và lịch sử không khác ứng dụng thật.
        var options = new DbContextOptionsBuilder<CommerceDbContext>();
        CommerceDbContext.ConfigureOptions(options, ConnectionString);
        return new CommerceDbContext(options.Options);
    }

    internal ActivityTrackingDbContext CreateActivityTrackingContext()
    {
        // Dùng cùng tùy chọn module để lịch sử không khác ứng dụng thật.
        var options = new DbContextOptionsBuilder<ActivityTrackingDbContext>();
        ActivityTrackingDbContext.ConfigureOptions(options, ConnectionString);
        return new ActivityTrackingDbContext(options.Options);
    }

    internal SupportDeskDbContext CreateSupportDeskContext()
    {
        // Dùng cùng tùy chọn module để lịch sử và ánh xạ enum không khác ứng dụng thật.
        var options = new DbContextOptionsBuilder<SupportDeskDbContext>();
        SupportDeskDbContext.ConfigureOptions(options, ConnectionString);
        return new SupportDeskDbContext(options.Options);
    }

    public async Task DisposeAsync()
    {
        // Đóng phiên cuối collection để PostgreSQL tự giải phóng khóa kể cả khi bài kiểm thử lỗi.
        if (_lockConnection is not null)
        {
            await _lockConnection.DisposeAsync();
            _lockConnection = null;
        }
        // Chỉ dọn dẹp pool kết nối mà factory và DbContext sử dụng để không ảnh hưởng collection khác.
        if (Initialized && !string.IsNullOrWhiteSpace(DatabaseUrl))
        {
            var pooledConnectionString = PostgresConnectionString.Convert(DatabaseUrl);
            ClearSpecificPool(pooledConnectionString);
        }
        Initialized = false;
    }
}

public class IdentityDatabaseFixture : IntegrationDatabaseFixture
{
    public IdentityDatabaseFixture() : base("identity")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class IdentityCollection : ICollectionFixture<IdentityDatabaseFixture>
{
    public const string Name = "Identity integration collection";
}

public class CatalogDatabaseFixture : IntegrationDatabaseFixture
{
    public CatalogDatabaseFixture() : base("catalog")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogCollection : ICollectionFixture<CatalogDatabaseFixture>
{
    public const string Name = "Catalog integration collection";
}

public class ShippingDatabaseFixture : IntegrationDatabaseFixture
{
    public ShippingDatabaseFixture() : base("shipping")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class ShippingCollection : ICollectionFixture<ShippingDatabaseFixture>
{
    public const string Name = "Shipping integration collection";
}

public class CommerceDatabaseFixture : IntegrationDatabaseFixture
{
    public CommerceDatabaseFixture() : base("commerce")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class CommerceCollection : ICollectionFixture<CommerceDatabaseFixture>
{
    public const string Name = "Commerce integration collection";
}

public class ActivityTrackingDatabaseFixture : IntegrationDatabaseFixture
{
    public ActivityTrackingDatabaseFixture() : base("activity")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class ActivityTrackingCollection : ICollectionFixture<ActivityTrackingDatabaseFixture>
{
    public const string Name = "ActivityTracking integration collection";
}

public class SupportDeskDatabaseFixture : IntegrationDatabaseFixture
{
    public SupportDeskDatabaseFixture() : base("support_desk")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class SupportDeskCollection : ICollectionFixture<SupportDeskDatabaseFixture>
{
    public const string Name = "SupportDesk integration collection";
}

public class AdminConsoleDatabaseFixture : IntegrationDatabaseFixture
{
    public AdminConsoleDatabaseFixture() : base("admin_console")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class AdminConsoleCollection : ICollectionFixture<AdminConsoleDatabaseFixture>
{
    public const string Name = "AdminConsole integration collection";
}

public class ApiDatabaseFixture : IntegrationDatabaseFixture
{
    public ApiDatabaseFixture() : base("api")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiDatabaseFixture>
{
    public const string Name = "Api integration collection";
}

public class DatabaseFixture : IntegrationDatabaseFixture
{
    public DatabaseFixture() : base("database")
    {
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database integration collection";
}
