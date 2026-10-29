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

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationDatabaseCollection : ICollectionFixture<IntegrationDatabaseFixture>
{
    public const string Name = "Exclusive PostgreSQL integration database";
}

public sealed class IntegrationDatabaseFixture : IAsyncLifetime
{
    private const long AdvisoryLockKey = 578524097137070905;
    private NpgsqlConnection? _lockConnection;
    public string DatabaseUrl { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;
    public bool Initialized { get; private set; }

    public async Task InitializeAsync()
    {
        // Từ chối DB phát triển trước mọi kết nối có thể thay đổi schema.
        DatabaseUrl = PickoryWebApplicationFactory.ResolveTestDatabaseUrl();
        var connection = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(DatabaseUrl));
        await ValidateTestTargetAsync(connection, PickoryWebApplicationFactory.ResolveEnvironmentValue("DATABASE_URL"));
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
        }
        // Chỉ fixture đã khởi tạo được phép đóng các kết nối nhàn rỗi của bộ kiểm thử.
        if (Initialized)
        {
            NpgsqlConnection.ClearAllPools();
        }
        Initialized = false;
    }
}
