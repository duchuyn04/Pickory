using Pickory.Modules.Catalog.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog.Events;

// Chạy thao tác ghi trong một transaction và chỉ phát sự kiện sau khi commit, giống `emitAfterCommit` của bản Node.
public static class CatalogTransaction
{
    public static async Task<T> RunAsync<T>(
        CatalogDbContext database,
        IEventDispatcher dispatcher,
        Func<DomainEventCollector, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var events = new DomainEventCollector(dispatcher);
        T result;
        // Thao tác lỗi thì transaction bị hủy khi dispose, và sự kiện đã gom không được phát.
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            result = await action(events);
            await transaction.CommitAsync(cancellationToken);
        }

        // Phát sau commit. Handler đọc DB từ kết nối khác vẫn thấy dữ liệu mới.
        await events.FlushAsync(cancellationToken);
        return result;
    }
}
