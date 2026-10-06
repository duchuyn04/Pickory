using Pickory.Shared.Events;

namespace Pickory.IntegrationTests.Infrastructure;

// Handler test nhận sự kiện qua dispatcher thật. Test xác nhận API phát sự kiện qua đúng đường đăng ký handler.
public sealed class RecordingDomainEventHandler<TEvent>(RecordedDomainEvents recordedEvents) : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    public Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default)
    {
        // Chỉ ghi lại sự kiện, không có tác dụng phụ khác. Kết quả của luồng nghiệp vụ không đổi.
        recordedEvents.Add(@event);
        return Task.CompletedTask;
    }
}
