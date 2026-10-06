using Npgsql;
using Pickory.Shared.Persistence;

namespace Pickory.Api.E2e;

// DB và admin fixture của suite E2E. Lệnh `e2e:start` xóa dữ liệu, nên mọi giá trị phải được kiểm tra trước.
public sealed record E2eTarget(string DatabaseUrl, string AdminEmail, string AdminPassword)
{
    public const string DatabaseName = "pickory_e2e";

    public static E2eTarget Read(string databaseUrl, string? e2eDatabaseUrl, string? adminEmail, string? adminPassword)
    {
        // Kiểm tra cả tên DB và biến riêng của E2E. Chạy nhầm lệnh với cấu hình dev không reset được DB dev.
        var databaseName = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(databaseUrl)).Database;
        if (databaseName != DatabaseName || e2eDatabaseUrl != databaseUrl)
        {
            throw new InvalidOperationException("E2E chỉ được migrate và reset DB pickory_e2e đã được cấu hình riêng.");
        }

        // Admin fixture chỉ dùng thông tin do Playwright cấp, không dùng admin của môi trường dev.
        if (string.IsNullOrWhiteSpace(adminEmail) || adminPassword is null || adminPassword.Length < 8)
        {
            throw new InvalidOperationException("Thiếu email hoặc mật khẩu admin fixture E2E hợp lệ.");
        }

        return new E2eTarget(databaseUrl, adminEmail, adminPassword);
    }
}
