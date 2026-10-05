using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.UnitTests;

// Kiểm tra hành vi phát sự kiện và khả năng cách ly lỗi giữa các handler.
public class EventDispatcherTests
{
    private record SampleUserCreatedEvent(string UserId) : IDomainEvent;

    private class FailingHandler : IDomainEventHandler<SampleUserCreatedEvent>
    {
        public Task HandleAsync(SampleUserCreatedEvent @event, CancellationToken cancellationToken = default)
        {
            // Cố tình ném lỗi để kiểm tra khả năng phục hồi của dispatcher.
            throw new InvalidOperationException("Mô phỏng handler gặp sự cố.");
        }
    }

    private class SuccessfulHandler : IDomainEventHandler<SampleUserCreatedEvent>
    {
        public bool WasHandled { get; private set; }

        public Task HandleAsync(SampleUserCreatedEvent @event, CancellationToken cancellationToken = default)
        {
            // Đánh dấu đã nhận sự kiện thành công khi luồng không bị gián đoạn.
            WasHandled = true;
            return Task.CompletedTask;
        }
    }

    private class TestLogger<T> : ILogger<T>
    {
        public bool HasLoggedError { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                // Ghi nhận sự kiện lỗi đã được chuyển tới logger thành công.
                HasLoggedError = true;
            }
        }
    }

    [Fact]
    // Handler ném lỗi chỉ ghi log và không ngăn cản các handler kế tiếp thực thi.
    public async Task PublishAsync_GhiLogVaKhongGayLoiLuongChinh_KhiCoHandlerThatBai()
    {
        var services = new ServiceCollection();
        var testLogger = new TestLogger<InProcessEventDispatcher>();
        var successfulHandler = new SuccessfulHandler();

        // Đăng ký cả hai handler vào container để mô phỏng môi trường thực tế.
        services.AddSingleton<IDomainEventHandler<SampleUserCreatedEvent>, FailingHandler>();
        services.AddSingleton<IDomainEventHandler<SampleUserCreatedEvent>>(successfulHandler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = new InProcessEventDispatcher(serviceProvider, testLogger);

        // Phát sự kiện mà không bị ngắt bởi ngoại lệ từ handler đầu tiên.
        var sampleEvent = new SampleUserCreatedEvent("user-123");
        await dispatcher.PublishAsync(sampleEvent);

        // Khẳng định handler thứ hai vẫn chạy và lỗi đã được ghi nhận vào log.
        Assert.True(successfulHandler.WasHandled);
        Assert.True(testLogger.HasLoggedError);
    }
}
