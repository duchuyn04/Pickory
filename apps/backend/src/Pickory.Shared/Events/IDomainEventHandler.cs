namespace Pickory.Shared.Events;

// Giao diện định nghĩa trình lắng nghe sự kiện miền.
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    // Tiếp nhận và thực thi tác vụ tương ứng khi sự kiện được phát ra.
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
