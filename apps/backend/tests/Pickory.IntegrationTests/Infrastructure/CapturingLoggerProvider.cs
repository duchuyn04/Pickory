using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pickory.IntegrationTests.Infrastructure;

// Ghi lại log của cả host test. Test dùng để kiểm cảnh báo và để chắc log không chứa secret hay dữ liệu cá nhân.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => _entries.ToList();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    // Mỗi dòng gồm thông điệp đã format và chuỗi lỗi. Test tìm secret trong cả hai.
    public sealed record CapturedLog(string Category, LogLevel Level, string Message, string? Exception);

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), exception?.ToString()));
        }
    }
}
