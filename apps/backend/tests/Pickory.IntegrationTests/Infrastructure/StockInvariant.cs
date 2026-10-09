using Npgsql;
using Xunit;

namespace Pickory.IntegrationTests.Infrastructure;

// Bất biến của tồn kho: `stock` của mọi SKU bằng tổng `delta` của các movement. Test catalog gọi hàm này trước khi dọn dữ liệu.
public static class StockInvariant
{
    public static async Task AssertHoldsAsync(IntegrationDatabaseFixture database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        // LEFT JOIN giữ cả SKU chưa có movement. SKU đó chỉ hợp lệ khi `stock` bằng 0.
        await using var command = new NpgsqlCommand(
            """
            SELECT s.code, s.stock, COALESCE(SUM(m.delta), 0)
            FROM skus s LEFT JOIN inventory_movements m ON m."skuId" = s.id
            GROUP BY s.id, s.code, s.stock
            HAVING s.stock <> COALESCE(SUM(m.delta), 0)
            ORDER BY s.code
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var violations = new List<string>();
        while (await reader.ReadAsync())
        {
            violations.Add($"SKU {reader.GetString(0)}: stock={reader.GetInt32(1)}, tổng delta={reader.GetInt64(2)}");
        }

        Assert.True(violations.Count == 0, "Bất biến stock = tổng delta bị vi phạm: " + string.Join("; ", violations));
    }
}
