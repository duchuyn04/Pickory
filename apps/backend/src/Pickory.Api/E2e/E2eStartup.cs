using Npgsql;
using Pickory.Modules.AiGateway;
using Pickory.Modules.Catalog;
using Pickory.Modules.Identity;
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

        // Dựng schema theo thứ tự của tài liệu migration. Migration AiGateway tạo extension `vector` dùng chung.
        await IdentityModule.MigrateIdentityDatabaseAsync(services, cancellationToken);
        await AiGatewayModule.MigrateAiGatewayDatabaseAsync(services, cancellationToken);
        await CatalogModule.MigrateCatalogDatabaseAsync(services, cancellationToken);

        // Chỉ tạo admin. Test phải tạo khách và nhân viên sales qua giao diện thật.
        var exitCode = await IdentityModule.RunIdentityCommandAsync(
            ["identity:create-admin", "--email", target.AdminEmail, "--password", target.AdminPassword, "--name", "Quản trị E2E"],
            services);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Không tạo được admin fixture E2E.");
        }

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
            DROP SCHEMA IF EXISTS public CASCADE;
            CREATE SCHEMA public AUTHORIZATION pg_database_owner;
            COMMENT ON SCHEMA public IS 'standard public schema';
            """, connection);
        await resetCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
