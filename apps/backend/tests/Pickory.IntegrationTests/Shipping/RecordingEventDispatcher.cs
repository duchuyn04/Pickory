using Pickory.Shared.Events;

namespace Pickory.IntegrationTests.Shipping;

// Bộ phát sự kiện giả ghi lại mọi sự kiện đã phát. Test dựng dịch vụ trực tiếp nên không cần bộ phát thật.
internal sealed class RecordingEventDispatcher : IEventDispatcher
{
    private readonly List<IDomainEvent> _published = [];
    private readonly object _gate = new();

    // Chạy ngay khi có sự kiện được phát. Test dùng để đọc DB từ kết nối khác đúng lúc phát và kiểm dữ liệu đã commit chưa.
    public Func<IDomainEvent, Task>? OnPublish { get; set; }

    public IReadOnlyList<IDomainEvent> Published
    {
        get
        {
            lock (_gate)
            {
                return _published.ToList();
            }
        }
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        lock (_gate)
        {
            _published.Add(@event);
        }

        if (OnPublish is not null)
        {
            await OnPublish(@event);
        }
    }
}
