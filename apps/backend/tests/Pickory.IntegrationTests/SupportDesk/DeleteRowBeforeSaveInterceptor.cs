using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;

namespace Pickory.IntegrationTests.SupportDesk;

// Xóa một mẫu bằng `DbContext` khác ngay trước lần `SaveChanges` đầu tiên sửa hoặc xóa mẫu đó.
// Cách này mô phỏng request khác xóa dòng giữa lúc request đang xét đọc và ghi, mà không cần chờ theo thời gian.
internal sealed class DeleteRowBeforeSaveInterceptor(Func<SupportDeskDbContext> createContext, string rowId)
    : SaveChangesInterceptor
{
    private int _alreadyDeleted;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var touchesRow = eventData.Context!.ChangeTracker.Entries<CannedResponse>()
            .Any(entry => entry.Entity.Id == rowId && entry.State is EntityState.Modified or EntityState.Deleted);
        // Chỉ xóa một lần, để lần lưu sau của cùng request không xóa lại dòng đã mất.
        if (!touchesRow || Interlocked.Exchange(ref _alreadyDeleted, 1) == 1)
        {
            return result;
        }

        await using var otherContext = createContext();
        await otherContext.CannedResponses.Where(row => row.Id == rowId).ExecuteDeleteAsync(cancellationToken);
        return result;
    }
}
