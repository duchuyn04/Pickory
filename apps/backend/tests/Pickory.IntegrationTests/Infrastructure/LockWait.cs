using Npgsql;

namespace Pickory.IntegrationTests.Infrastructure;

// Chờ đến khi có một phiên DB đang bị chặn bởi khóa. Test dùng hàm này thay cho mốc chờ cố định.
public static class LockWait
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public static async Task UntilSessionIsBlockedAsync(IntegrationDatabaseFixture database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        // Chỉ đếm phiên của chính DB test, trừ phiên đang truy vấn.
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid() AND wait_event_type = 'Lock'
            """, connection);
        var deadline = DateTime.UtcNow + Limit;
        while (Convert.ToInt64(await command.ExecuteScalarAsync()) == 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Không có phiên DB nào chờ khóa sau 10 giây.");
            }

            await Task.Delay(10);
        }
    }
}
