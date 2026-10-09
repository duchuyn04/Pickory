using System.Collections.Concurrent;
using Pickory.Shared.Events;

namespace Pickory.IntegrationTests.Infrastructure;

// Ghi lại sự kiện mà API đã phát. Test kiểm tra số lần phát và dữ liệu đi kèm.
public sealed class RecordedDomainEvents
{
    // Dùng hàng đợi an toàn luồng. Handler có thể chạy từ nhiều request cùng lúc.
    private readonly ConcurrentQueue<IDomainEvent> _events = new();

    public void Add(IDomainEvent domainEvent)
    {
        _events.Enqueue(domainEvent);
    }

    // Lọc theo loại sự kiện. Mỗi test chỉ kiểm tra loại sự kiện mà luồng của test phát.
    public IReadOnlyList<TEvent> OfType<TEvent>()
        where TEvent : IDomainEvent
    {
        return _events.OfType<TEvent>().ToList();
    }
}
