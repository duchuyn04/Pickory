using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog.Events;

// Gom sự kiện trong lúc transaction đang chạy, rồi phát sau khi commit.
// Module khác không nhận sự kiện về dữ liệu chưa commit hoặc đã bị hủy.
public sealed class CatalogEventCollector(IEventDispatcher dispatcher)
{
    private List<Func<CancellationToken, Task>> _queued = [];

    public void Emit<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent
    {
        _queued.Add(cancellationToken => dispatcher.PublishAsync(domainEvent, cancellationToken));
    }

    // Bỏ hàng đợi khi transaction bị hủy. Sự kiện của thao tác bị hủy không bao giờ được phát.
    public void Discard()
    {
        _queued = [];
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        // Lấy hàng đợi ra trước khi phát. Gọi lần hai không phát lại sự kiện cũ.
        var toPublish = _queued;
        _queued = [];
        foreach (var publish in toPublish)
        {
            await publish(cancellationToken);
        }
    }
}
