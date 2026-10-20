using Npgsql;
using Pickory.Shared.Configuration;
using Pickory.Shared.Persistence;

namespace Pickory.Api.Deployment;

// DB cũ còn ba bảng và schema lịch sử của module AI đã gỡ. Bước này dọn chúng trong `db:migrate`, và tên còn chữ AiGateway vì nó nhắc tới module cũ.
public static class AiGatewayCleanup
{
    public const string StepName = "AiGatewayCleanup";

    public static async Task RunAsync(AppConfig config, CancellationToken cancellationToken)
    {
        // Không dùng pool. Kết nối dọn dẹp chỉ sống trong bước này và không giữ cache kiểu của schema vừa xóa.
        var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(config.DatabaseUrl)) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Một giao dịch giữ cả hai lệnh xóa. Lỗi ở lệnh thứ hai không để lại DB mất bảng mà còn schema lịch sử.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // `IF EXISTS` cho phép chạy lại và chạy trên DB trống. Không xóa extension `vector`, vì DB đang chạy giữ nó.
        await using var dropObjectsCommand = new NpgsqlCommand("""
            DROP TABLE IF EXISTS public.ai_usage_logs, public.ai_settings, public.ai_embedding_cache;
            DROP SCHEMA IF EXISTS ef_ai_gateway CASCADE;
            """, connection, transaction);
        await dropObjectsCommand.ExecuteNonQueryAsync(cancellationToken);

        // Chỉ ghi nhận thay đổi sau khi cả hai lệnh thành công.
        await transaction.CommitAsync(cancellationToken);
    }
}
