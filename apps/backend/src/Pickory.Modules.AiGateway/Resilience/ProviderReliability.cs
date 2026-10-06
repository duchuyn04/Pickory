using System.Runtime.CompilerServices;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;

namespace Pickory.Modules.AiGateway.Resilience;

// Retry, timeout và circuit breaker cho mọi lời gọi provider, đặt ở một chỗ.
// SDK của provider tắt retry riêng, nên số lần gọi ra ngoài chỉ do lớp này quyết định.
public sealed class ProviderReliability(TimeProvider timeProvider, Func<double> random)
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan FirstTokenTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ConsumerTimeout = TimeSpan.FromSeconds(30);

    public CircuitBreaker Circuit { get; } = new(timeProvider);

    public ProviderReliability(TimeProvider timeProvider)
        : this(timeProvider, Random.Shared.NextDouble)
    {
    }

    // Kiểm tra sớm trước khi đọc DB. Mạch đang mở thì trả lỗi ngay.
    public void AssertAvailable(string provider)
    {
        Circuit.AssertAvailable(provider);
    }

    public async Task<T> RunAsync<T>(
        string provider,
        TimeSpan timeout,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            // Lấy lease ngoài khối bắt lỗi. Mạch mở thì dừng ngay, không thử lại và không chờ backoff.
            var lease = Circuit.Acquire(provider);
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                var result = await RunWithDeadlineAsync(operation, timeout, attemptCancellation, cancellationToken);
                Circuit.Success(provider, lease);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Người gọi hủy request. Đây không phải lỗi của provider, nên chỉ nhả probe.
                Circuit.Release(provider, lease);
                throw;
            }
            catch (Exception exception)
            {
                // Hủy lần thử hiện tại để provider dừng request đã bị bỏ.
                // Hủy đồng bộ giữ timer backoff được tạo ngay sau lỗi, nên thời điểm thử lại cố định.
                attemptCancellation.Cancel();
                RecordFailure(provider, lease, exception);
                // Probe chỉ có một request. Lỗi thường chỉ được thử lại tối đa 2 lần.
                if (!IsRetryable(exception) || lease.IsProbe || attempt == MaxAttempts - 1)
                {
                    throw new AiUnavailableException(AiErrorCodes.From(exception));
                }

                // Mạch vừa mở do các request khác thì dừng, không chờ backoff vô ích.
                AssertAvailableOrThrow(provider, exception);
                await Task.Delay(BackoffDelay(attempt), timeProvider, cancellationToken);
            }
        }
    }

    // Stream có token đầu trong 10 giây và không im quá 15 giây giữa hai chunk.
    // Stream khỏe không có giới hạn tổng thời gian, nên câu trả lời dài không bị cắt.
    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        string provider,
        Func<CancellationToken, IAsyncEnumerable<ChatChunk>> operation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var lease = Circuit.Acquire(provider);
            var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IAsyncEnumerator<ChatChunk>? iterator = null;
            ITimer? consumerTimer = null;
            var emitted = false;
            Exception? failure = null;
            try
            {
                var finished = false;
                var startTimestamp = timeProvider.GetTimestamp();
                try
                {
                    iterator = operation(attemptCancellation.Token).GetAsyncEnumerator(attemptCancellation.Token);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                while (failure is null)
                {
                    // Chỉ đếm thời gian chờ provider. Thời gian người đọc giữ một chunk không tính vào timeout của provider.
                    var timeout = emitted ? IdleTimeout : FirstTokenTimeout - timeProvider.GetElapsedTime(startTimestamp);
                    var (hasNext, error) = await MoveNextWithDeadlineAsync(iterator!, timeout, attemptCancellation, cancellationToken);
                    if (error is not null)
                    {
                        failure = error;
                        break;
                    }

                    if (!hasNext)
                    {
                        // Stream đóng mà thiếu phần kết thúc có usage là lỗi. Gateway không ghi chi phí bằng 0.
                        if (!finished)
                        {
                            failure = new ProviderException(null, retryable: true, code: "PROVIDER_INCOMPLETE_STREAM");
                            break;
                        }

                        Circuit.Success(provider, lease);
                        yield break;
                    }

                    var chunk = iterator!.Current;
                    // Chunk text rỗng không phải token. Bộ đếm token đầu không được đặt lại vì chunk rỗng.
                    if (chunk is TextChatChunk { Text.Length: 0 })
                    {
                        continue;
                    }

                    emitted = true;
                    finished |= chunk is FinishChatChunk;
                    // Người đọc im 30 giây thì hủy stream và nhả probe. Việc này không tính là lỗi của provider.
                    consumerTimer = timeProvider.CreateTimer(
                        _ =>
                        {
                            attemptCancellation.Cancel();
                            Circuit.Release(provider, lease);
                        },
                        null,
                        ConsumerTimeout,
                        Timeout.InfiniteTimeSpan);
                    yield return chunk;
                    consumerTimer.Dispose();
                    consumerTimer = null;

                    if (attemptCancellation.IsCancellationRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        failure = new ProviderException(null, retryable: false, code: "STREAM_CONSUMER_TIMEOUT");
                    }
                }

                // Ghi lỗi trước khi khối `finally` nhả lease. Circuit bỏ qua kết quả của probe đã nhả.
                RecordFailure(provider, lease, failure!);
            }
            finally
            {
                consumerTimer?.Dispose();
                attemptCancellation.Cancel();
                Circuit.Release(provider, lease);
                // Không chờ iterator đóng. Provider treo có thể bỏ qua token hủy và giữ luồng mãi.
                _ = DisposeQuietlyAsync(iterator, attemptCancellation);
            }

            // Đã gửi text cho người đọc thì không thử lại. Thử lại sẽ lặp nội dung người đọc đã nhận.
            if (emitted || !IsRetryable(failure) || lease.IsProbe || attempt == MaxAttempts - 1)
            {
                throw new AiUnavailableException(AiErrorCodes.From(failure!));
            }

            AssertAvailableOrThrow(provider, failure!);
            await Task.Delay(BackoffDelay(attempt), timeProvider, cancellationToken);
        }
    }

    // Lần thử thứ nhất chờ 500 ms, lần thứ hai chờ 1500 ms, cộng jitter dưới 100 ms.
    private TimeSpan BackoffDelay(int attempt)
    {
        var baseMilliseconds = attempt == 0 ? 500 : 1500;
        return TimeSpan.FromMilliseconds(baseMilliseconds + Math.Floor(random() * 100));
    }

    private void AssertAvailableOrThrow(string provider, Exception lastError)
    {
        try
        {
            Circuit.AssertAvailable(provider);
        }
        catch (AiUnavailableException)
        {
            // Giữ mã lỗi của lần thử cuối. Log cho biết lỗi thật của provider, không chỉ mạch mở.
            throw new AiUnavailableException(AiErrorCodes.From(lastError));
        }
    }

    // Lỗi do request sai (4xx) không phải sự cố provider, nên không làm tăng bộ đếm mở mạch.
    private void RecordFailure(string provider, CircuitLease lease, Exception exception)
    {
        if (IsRetryable(exception))
        {
            Circuit.Failure(provider, lease);
            return;
        }

        Circuit.Release(provider, lease);
    }

    private static bool IsRetryable(Exception? exception)
    {
        return exception is ProviderException { Retryable: true };
    }

    private async Task<T> RunWithDeadlineAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationTokenSource attemptCancellation,
        CancellationToken callerToken)
    {
        Task<T> operationTask;
        try
        {
            operationTask = operation(attemptCancellation.Token);
        }
        catch (Exception exception)
        {
            // Lỗi ném đồng bộ được coi như lỗi của task. Retry và circuit breaker vẫn áp dụng.
            operationTask = Task.FromException<T>(exception);
        }

        // Chạy đua với timer. Provider không tôn trọng token hủy vẫn không giữ gateway quá thời gian chờ.
        var deadline = Task.Delay(timeout, timeProvider, attemptCancellation.Token);
        var finished = await Task.WhenAny(operationTask, deadline);
        if (finished == operationTask)
        {
            // Hủy timer còn chạy. Timer không được hủy nhầm một lời gọi đã xong.
            attemptCancellation.Cancel();
            return await operationTask;
        }

        callerToken.ThrowIfCancellationRequested();
        ObserveFault(operationTask);
        throw new ProviderException(null, retryable: true, code: "PROVIDER_TIMEOUT");
    }

    private async Task<(bool HasNext, Exception? Error)> MoveNextWithDeadlineAsync(
        IAsyncEnumerator<ChatChunk> iterator,
        TimeSpan timeout,
        CancellationTokenSource attemptCancellation,
        CancellationToken callerToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return (false, new ProviderException(null, retryable: true, code: "PROVIDER_TIMEOUT"));
        }

        Task<bool> moveTask;
        try
        {
            moveTask = iterator.MoveNextAsync().AsTask();
        }
        catch (Exception exception)
        {
            moveTask = Task.FromException<bool>(exception);
        }

        // Timer dừng khi người gọi hủy. Gateway trả lời ngay thay vì chờ hết thời gian của provider.
        using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        var deadline = Task.Delay(timeout, timeProvider, deadlineCancellation.Token);
        var finishedTask = await Task.WhenAny(moveTask, deadline);
        if (finishedTask == moveTask)
        {
            deadlineCancellation.Cancel();
            try
            {
                return (await moveTask, null);
            }
            catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return (false, exception);
            }
        }

        callerToken.ThrowIfCancellationRequested();
        attemptCancellation.Cancel();
        ObserveFault(moveTask);
        return (false, new ProviderException(null, retryable: true, code: "PROVIDER_TIMEOUT"));
    }

    private static async Task DisposeQuietlyAsync(IAsyncEnumerator<ChatChunk>? iterator, CancellationTokenSource attemptCancellation)
    {
        try
        {
            if (iterator is not null)
            {
                await iterator.DisposeAsync();
            }
        }
        catch (Exception)
        {
            // Iterator đang chờ provider không đóng được ngay. Token đã hủy sẽ dừng provider.
        }
        finally
        {
            attemptCancellation.Dispose();
        }
    }

    // Task bị bỏ vẫn có thể lỗi sau này. Đọc lỗi đó để .NET không báo exception chưa được quan sát.
    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static faulted => faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
