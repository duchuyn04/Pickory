using Npgsql;
using Pickory.Modules.Catalog;
using Pickory.Modules.Commerce;
using Pickory.Modules.Identity;
using Pickory.Modules.Shipping;
using Pickory.Shared.Persistence;

namespace Pickory.Api.E2e;

// Chuẩn bị DB cho Playwright trước khi API nhận request. Lệnh thay `scripts/e2e-start.ts` của bản Node đã xóa.
public static class E2eStartup
{
    public const string Command = "e2e:start";

    public static bool IsCommand(IReadOnlyList<string> args)
    {
        return args.Count > 0 && args[0] == Command;
    }

    public static async Task PrepareAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        // Kiểm tra đích trước mọi thao tác ghi. Cấu hình sai không reset được DB dev.
        var target = E2eTarget.Read(
            configuration["DATABASE_URL"] ?? string.Empty,
            configuration["E2E_DATABASE_URL"],
            configuration["E2E_ADMIN_EMAIL"],
            configuration["E2E_ADMIN_PASSWORD"]);
        var outboxDirectory = configuration["DEV_OUTBOX_DIR"]
            ?? throw new InvalidOperationException("E2E cần DEV_OUTBOX_DIR để tách thư của suite khỏi thư dev.");

        await CreateDatabaseIfMissingAsync(target, cancellationToken);
        await ResetSchemasAsync(target, cancellationToken);

        // Dựng schema theo thứ tự của tài liệu migration.
        await IdentityModule.MigrateIdentityDatabaseAsync(services, cancellationToken);
        await CatalogModule.MigrateCatalogDatabaseAsync(services, cancellationToken);
        await ShippingModule.MigrateShippingDatabaseAsync(services, cancellationToken);
        await CommerceModule.MigrateCommerceDatabaseAsync(services, cancellationToken);

        // Nạp danh mục địa chỉ và chờ xong, vì worker nền chạy song song với test và có thể chưa kịp ghi.
        // Lỗi ở đây dừng lệnh thay vì mở API với danh mục rỗng.
        await ShippingModule.SyncAddressesAsync(services, cancellationToken);

        // Tạo tài khoản quản trị và các tài khoản khách hàng cố định cho E2E.
        // Đăng ký qua giao diện có giới hạn theo IP nên kịch bản không kiểm tra đăng ký dùng tài khoản nạp sẵn.
        var exitCode = await IdentityModule.RunIdentityCommandAsync(
            ["identity:create-admin", "--email", target.AdminEmail, "--password", target.AdminPassword, "--name", "Quản trị E2E"],
            services);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Không tạo được admin fixture E2E.");
        }

        var seedCustomers = new (string Email, string Name, string Phone)[]
        {
            ("e2e-lock-customer@pickory.local", "Khách Khóa E2E", "0987654321"),
            ("e2e-screens-customer@pickory.local", "Khách Giao Diện E2E", "0987654321"),
            ("e2e-orders-customer@pickory.local", "Khách Đơn Hàng E2E", "0987654321"),
            ("e2e-seed-verification@pickory.local", "Khách Xác Thực Seed E2E", "0987654321"),
            // Mỗi khách chỉ đánh giá được một lần cho một sản phẩm, nên spec kiểm duyệt đánh giá cần ba khách riêng.
            ("e2e-reviews-visible@pickory.local", "Khách Đánh Giá Hiện E2E", "0987654321"),
            ("e2e-reviews-hidden@pickory.local", "Khách Đánh Giá Ẩn E2E", "0987654321"),
            ("e2e-reviews-unreceived@pickory.local", "Khách Chưa Nhận Hàng E2E", "0987654321"),
        };
        var accountPassword = configuration["E2E_ACCOUNT_PASSWORD"]
            ?? throw new InvalidOperationException("E2E cần E2E_ACCOUNT_PASSWORD để tạo tài khoản khách fixture.");

        // Tạo tài khoản khách đã xác thực email để kịch bản không chạm trần giới hạn đăng ký.
        await IdentityModule.SeedE2eCustomersAsync(services, seedCustomers, accountPassword, cancellationToken);

        // Xóa thư cũ của suite. Test đọc link mời mới nhất mà không nhầm với thư của lần chạy trước.
        E2eOutbox.ClearOwnedMessages(outboxDirectory);
    }

    private static async Task CreateDatabaseIfMissingAsync(E2eTarget target, CancellationToken cancellationToken)
    {
        // Kết nối DB hệ thống `postgres` để tạo DB E2E trên volume PostgreSQL có sẵn.
        var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(target.DatabaseUrl))
        {
            Database = "postgres",
            Pooling = false,
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var existsCommand = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        existsCommand.Parameters.AddWithValue("name", E2eTarget.DatabaseName);
        if (await existsCommand.ExecuteScalarAsync(cancellationToken) is null)
        {
            // Tên DB là hằng số trong code. Lệnh không ghép giá trị từ biến môi trường vào SQL.
            await using var createCommand = new NpgsqlCommand($"CREATE DATABASE \"{E2eTarget.DatabaseName}\"", connection);
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ResetSchemasAsync(E2eTarget target, CancellationToken cancellationToken)
    {
        // Không dùng pool. Kết nối cũ trong pool có thể giữ cache kiểu dữ liệu của schema bị xóa.
        var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(target.DatabaseUrl)) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Xóa schema của ứng dụng, kể cả schema Prisma cũ. Migration EF Core dựng lại toàn bộ trên DB trống.
        await using var resetCommand = new NpgsqlCommand("""
            DROP SCHEMA IF EXISTS ef_identity CASCADE;
            DROP SCHEMA IF EXISTS ef_ai_gateway CASCADE;
            DROP SCHEMA IF EXISTS ef_catalog CASCADE;
            DROP SCHEMA IF EXISTS ef_shipping CASCADE;
            DROP SCHEMA IF EXISTS ef_commerce CASCADE;
            DROP SCHEMA IF EXISTS public CASCADE;
            CREATE SCHEMA public AUTHORIZATION pg_database_owner;
            COMMENT ON SCHEMA public IS 'standard public schema';
            """, connection);
        await resetCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
