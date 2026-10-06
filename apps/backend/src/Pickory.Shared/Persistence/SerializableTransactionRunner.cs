using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Shared.Exceptions;

namespace Pickory.Shared.Persistence;

// Chạy thao tác trong giao dịch `Serializable`. Hai thao tác đồng thời không cùng vượt một bước kiểm tra dựa trên số đếm.
public static class SerializableTransactionRunner
{
    public static async Task<TResult> RunAsync<TResult>(DbContext database, Func<Task<TResult>> work, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var result = await work();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            // PostgreSQL hủy một trong hai giao dịch xung đột. Trả 409 để client biết có thể thử lại, giống bản Node.
            throw new AppException("CONFLICT", 409, "Xung đột giao dịch đồng thời, vui lòng thử lại.");
        }
    }

    public static Task RunAsync(DbContext database, Func<Task> work, CancellationToken cancellationToken)
    {
        // Dùng chung một đường chạy với bản có kết quả. Cách đổi lỗi xung đột thành 409 chỉ nằm ở một chỗ.
        return RunAsync(database, async () =>
        {
            await work();
            return true;
        }, cancellationToken);
    }

    // EF Core có thể bọc lỗi của PostgreSQL trong exception khác. Hàm tìm mã lỗi trong cả chuỗi exception.
    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }
}
