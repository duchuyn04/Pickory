using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pickory.IntegrationTests.Infrastructure;

// Làm lệnh SQL đầu tiên khớp mẫu thất bại một lần. Test giả lập DB lỗi khi đọc hoặc ghi một bảng.
public sealed class FailOnceCommandInterceptor(string commandStart, string errorMessage) : DbCommandInterceptor
{
    private int _failed;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        FailFirstMatch(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        FailFirstMatch(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void FailFirstMatch(DbCommand command)
    {
        var matches = command.CommandText.TrimStart().StartsWith(commandStart, StringComparison.OrdinalIgnoreCase);
        // Chỉ lỗi một lần. Lần gọi sau phải phục hồi được.
        if (matches && Interlocked.Exchange(ref _failed, 1) == 0)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }
}
