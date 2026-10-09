using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.UnitTests.Shared.Events;

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

    // Singleton ghi lại sự kiện mà handler scoped đã nhận. Test đọc kết quả sau khi scope đã đóng.
    private sealed class HandledEventProbe
    {
        public List<string> HandledUserIds { get; } = [];
    }

    // Handler dùng `DbContext` có vòng đời scoped. Handler mẫu này mô phỏng phụ thuộc đó.
    private sealed class ScopedHandler(HandledEventProbe probe) : IDomainEventHandler<SampleUserCreatedEvent>
    {
        public Task HandleAsync(SampleUserCreatedEvent @event, CancellationToken cancellationToken = default)
        {
            // Ghi mã người dùng vào probe. Test dùng giá trị này để biết handler đã chạy.
            probe.HandledUserIds.Add(@event.UserId);
            return Task.CompletedTask;
        }
    }

    [Fact]
    // Dispatcher singleton chạy được handler scoped. Mỗi lần phát sự kiện tạo scope riêng thay vì dùng root provider.
    public async Task PublishAsync_RunsScopedHandlerFromSingletonDispatcher()
    {
        var services = new ServiceCollection();
        var probe = new HandledEventProbe();
        services.AddSingleton(probe);
        // Đăng ký handler scoped, giống handler thật cần `DbContext`.
        services.AddScoped<IDomainEventHandler<SampleUserCreatedEvent>, ScopedHandler>();
        // Môi trường Development bật kiểm tra scope. Root provider từ chối cấp dịch vụ scoped.
        var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var dispatcher = new InProcessEventDispatcher(serviceProvider, new TestLogger<InProcessEventDispatcher>());

        // Phát sự kiện không ném lỗi scope.
        await dispatcher.PublishAsync(new SampleUserCreatedEvent("user-456"));

        // Handler chạy đúng một lần với đúng dữ liệu.
        Assert.Equal(["user-456"], probe.HandledUserIds);
    }
}
