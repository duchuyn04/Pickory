using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Resilience;

// Port phần `run` của `reliability.test.ts` ở bản Node. Test dùng đồng hồ giả cho backoff và timeout.
public sealed class ProviderReliabilityTests
{
    // Mỗi test có giới hạn 10 giây. Lỗi làm request treo thành test đỏ thay vì treo cả bộ test.
    private static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(30);

    private static (FakeTimeProvider Time, ProviderReliability Reliability) Create()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));
        // Jitter bằng 0 để thời điểm thử lại cố định: 500 ms rồi 1500 ms.
        return (time, new ProviderReliability(time, () => 0));
    }

    [Fact(Timeout = 10_000)]
    // Chỉ thử lại lỗi tạm thời, tối đa 3 lần. Lỗi 4xx không được thử lại.
    public async Task RunAsync_RetriesOnlyTransientErrorsWithBackoff()
    {
        var (time, reliability) = Create();
        var attempts = 0;
        var pending = reliability.RunAsync("deepseek", ChatTimeout, _ =>
        {
            attempts++;
            return attempts < 3
                ? Task.FromException<string>(new ProviderException(429, retryable: true))
                : Task.FromResult("ok");
        }, CancellationToken.None);

        await FakeTimeDriver.WaitUntilAsync(() => attempts == 1);
        time.Advance(TimeSpan.FromMilliseconds(499));
        await Task.Delay(50);
        Assert.Equal(1, attempts);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await FakeTimeDriver.WaitUntilAsync(() => attempts == 2);
        time.Advance(TimeSpan.FromMilliseconds(1500));

        Assert.Equal("ok", await pending);
        Assert.Equal(3, attempts);

        var rejectedCalls = 0;
        await Assert.ThrowsAsync<AiUnavailableException>(() => reliability.RunAsync<string>("deepseek", ChatTimeout, _ =>
        {
            rejectedCalls++;
            throw new ProviderException(400, retryable: false);
        }, CancellationToken.None));
        Assert.Equal(1, rejectedCalls);
    }

    [Fact(Timeout = 10_000)]
    // 5 lỗi liên tiếp mở mạch. Trong 60 giây không gọi ra ngoài, sau đó chỉ một probe được chạy.
    public async Task RunAsync_FiveFailuresOpenCircuitThenAllowOneProbe()
    {
        var (time, reliability) = Create();
        var failureCalls = 0;
        Func<CancellationToken, Task<string>> failure = _ =>
        {
            failureCalls++;
            return Task.FromException<string>(new ProviderException(503, retryable: true));
        };

        var first = reliability.RunAsync("deepseek", ChatTimeout, failure, CancellationToken.None);
        await FakeTimeDriver.AdvanceUntilCompletedAsync(time, first, TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<AiUnavailableException>(() => first);
        var second = reliability.RunAsync("deepseek", ChatTimeout, failure, CancellationToken.None);
        await FakeTimeDriver.AdvanceUntilCompletedAsync(time, second, TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<AiUnavailableException>(() => second);
        Assert.Equal(5, failureCalls);

        var blockedCalls = 0;
        Func<CancellationToken, Task<string>> blocked = _ =>
        {
            blockedCalls++;
            return Task.FromResult("unexpected");
        };
        await Assert.ThrowsAsync<AiUnavailableException>(() => reliability.RunAsync("deepseek", ChatTimeout, blocked, CancellationToken.None));
        Assert.Equal(0, blockedCalls);

        time.Advance(TimeSpan.FromSeconds(60));
        var probeResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = reliability.RunAsync("deepseek", ChatTimeout, _ => probeResult.Task, CancellationToken.None);
        await Assert.ThrowsAsync<AiUnavailableException>(() => reliability.RunAsync("deepseek", ChatTimeout, blocked, CancellationToken.None));
        probeResult.SetResult("recovered");

        Assert.Equal("recovered", await probe);
        Assert.Equal("closed", await reliability.RunAsync("deepseek", ChatTimeout, _ => Task.FromResult("closed"), CancellationToken.None));
    }

    [Fact(Timeout = 10_000)]
    // Provider không tự kết thúc. Timeout hủy token của từng lần thử và trả lỗi an toàn sau 3 lần thử.
    public async Task RunAsync_TimeoutCancelsEachAttempt()
    {
        var (time, reliability) = Create();
        var tokens = new List<CancellationToken>();
        var pending = reliability.RunAsync<string>("openai", TimeSpan.FromSeconds(15), token =>
        {
            tokens.Add(token);
            return new TaskCompletionSource<string>().Task;
        }, CancellationToken.None);

        await FakeTimeDriver.AdvanceUntilCompletedAsync(time, pending, TimeSpan.FromMilliseconds(500));

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => pending);
        Assert.Equal("PROVIDER_TIMEOUT", error.ReasonCode);
        Assert.Equal(3, tokens.Count);
        Assert.All(tokens, token => Assert.True(token.IsCancellationRequested));
    }

    [Fact(Timeout = 10_000)]
    // Người gọi hủy request thì gateway dừng ngay. Việc hủy không được tính là lỗi của provider.
    public async Task RunAsync_CallerCancellationDoesNotCountAsProviderFailure()
    {
        var (_, reliability) = Create();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var cancellation = new CancellationTokenSource();
            var pending = reliability.RunAsync<string>("deepseek", ChatTimeout, _ => new TaskCompletionSource<string>().Task, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }

        Assert.Equal("ok", await reliability.RunAsync("deepseek", ChatTimeout, _ => Task.FromResult("ok"), CancellationToken.None));
    }
}
