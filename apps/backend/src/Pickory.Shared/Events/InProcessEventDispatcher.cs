using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pickory.Shared.Events;

// Bộ điều phối sự kiện trong tiến trình bảo đảm cách ly ranh giới giữa các module.
public class InProcessEventDispatcher : IEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InProcessEventDispatcher> _logger;

    public InProcessEventDispatcher(
        IServiceProvider serviceProvider,
        ILogger<InProcessEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        // Mỗi lần phát dùng scope riêng. Handler cần `DbContext` scoped không lấy được dịch vụ từ root provider.
        await using var scope = _serviceProvider.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetServices<IDomainEventHandler<TEvent>>();

        foreach (var handler in handlers)
        {
            try
            {
                // Thực thi tác vụ của trình lắng nghe theo thứ tự tuần tự.
                await handler.HandleAsync(@event, cancellationToken);
            }
            catch (Exception exception)
            {
                // Ghi log lỗi để bảo vệ luồng nghiệp vụ không bị gián đoạn.
                _logger.LogError(
                    exception,
                    "Trình lắng nghe sự kiện {EventName} ném ra ngoại lệ thất bại.",
                    typeof(TEvent).Name);
            }
        }
    }
}
