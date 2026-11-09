using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.IntegrationTests.SupportDesk;

// Giữ mọi `SaveChanges` có thêm `CannedResponse` lại tới khi đủ số request cùng tới, rồi thả tất cả cùng lúc.
// Nhờ đó test đua không dựa vào thời gian chờ: hai lệnh `INSERT` luôn chồng nhau.
internal sealed class ConcurrentSaveGate(int participants) : SaveChangesInterceptor
{
    // Hạn chờ để test đỏ thay vì treo, khi một request không bao giờ tới được `SaveChanges`.
    private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(20);

    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivedCount;

    public int ArrivedCount => Volatile.Read(ref _arrivedCount);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var addsTemplate = eventData.Context!.ChangeTracker.Entries<CannedResponse>()
            .Any(entry => entry.State == EntityState.Added);
        if (!addsTemplate)
        {
            return result;
        }

        // Request cuối cùng tới sẽ thả cổng cho tất cả.
        if (Interlocked.Increment(ref _arrivedCount) >= participants)
        {
            _allArrived.TrySetResult();
        }

        await _allArrived.Task.WaitAsync(ArrivalTimeout, cancellationToken);
        return result;
    }
}
