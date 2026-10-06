using Microsoft.Extensions.Logging;

namespace Pickory.UnitTests.TestDoubles;

// Logger ghi lại mức và nội dung của mỗi lần gọi. Test kiểm tra code có ghi lỗi hoặc lộ bí mật hay không.
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<LogLevel> RecordedLevels { get; } = [];
    public List<string> RecordedMessages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        RecordedLevels.Add(logLevel);
        // Ghi cả exception. Bí mật có thể nằm trong exception chứ không chỉ trong câu log.
        RecordedMessages.Add($"{formatter(state, exception)} {exception}");
    }
}
