namespace Pickory.Shared.Events;

// Giao diện điều phối sự kiện miền tới các trình lắng nghe trong tiến trình.
public interface IEventDispatcher
{
    // Phát tán sự kiện tới toàn bộ các trình lắng nghe đã đăng ký.
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}
