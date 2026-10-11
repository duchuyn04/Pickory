using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Pickory.Modules.Commerce.Persistence;

namespace Pickory.Modules.Commerce.Carts;

// Chèn một dòng giỏ bằng SQL thô. Giỏ hàng và gộp giỏ dùng chung để cùng một cách chống tạo trùng.
internal static class CartInserter
{
    // `ON CONFLICT DO NOTHING` bỏ qua cả hai unique index (`UserId` và `AnonymousId`) mà không làm hỏng transaction.
    public static async Task InsertIfMissingAsync(
        CommerceDbContext database, TimeProvider clock, string? userId, string? anonymousId,
        CancellationToken cancellationToken)
    {
        // Cột `updatedAt` là `timestamp` không múi giờ. Khai báo kiểu tường minh để Npgsql không gửi `timestamptz` theo múi giờ phiên.
        var updatedAt = new NpgsqlParameter("updatedAt", NpgsqlDbType.Timestamp)
        {
            Value = DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified),
        };
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO carts ("id", "anonymousId", "userId", "updatedAt")
            VALUES (@id, @anonymousId, @userId, @updatedAt)
            ON CONFLICT DO NOTHING
            """,
            [
                new NpgsqlParameter("id", Guid.NewGuid().ToString()),
                new NpgsqlParameter("anonymousId", NpgsqlDbType.Text) { Value = (object?)anonymousId ?? DBNull.Value },
                new NpgsqlParameter("userId", NpgsqlDbType.Text) { Value = (object?)userId ?? DBNull.Value },
                updatedAt,
            ],
            cancellationToken);
    }
}
