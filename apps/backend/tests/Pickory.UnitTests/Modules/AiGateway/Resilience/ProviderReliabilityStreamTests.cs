using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Resilience;

// Port phần stream của `reliability.test.ts` và `gateway-regressions.test.ts` ở bản Node.
// Mỗi test có giới hạn 10 giây, nên lỗi làm stream treo thành test đỏ thay vì treo cả bộ test.
public sealed class ProviderReliabilityStreamTests
{
    private static readonly TokenUsage Usage = new(10, 5, 0);

    private static (FakeTimeProvider Time, ProviderReliability Reliability) Create()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));
        return (time, new ProviderReliability(time, () => 0));
    }

    private static void RecordFailures(ProviderReliability reliability, int count)
    {
        for (var failure = 0; failure < count; failure++)
        {
            reliability.Circuit.Failure("deepseek", reliability.Circuit.Acquire("deepseek"));
        }
    }

    [Fact(Timeout = 10_000)]
    // Stream đã gửi text thì không thử lại khi lỗi giữa chừng. Thử lại sẽ lặp nội dung khách đã nhận.
    public async Task StreamAsync_DoesNotRetryAfterText()
    {
        var (_, reliability) = Create();
        var attempts = 0;

        async IAsyncEnumerable<ChatChunk> Operation([EnumeratorCancellation] CancellationToken token)
        {
            attempts++;
            await Task.Yield();
            yield return new TextChatChunk("hello");
            throw new ProviderException(500, retryable: true);
        }

        await using var iterator = reliability.StreamAsync("deepseek", Operation, CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(new TextChatChunk("hello"), iterator.Current);
        var error = await Assert.ThrowsAsync<AiUnavailableException>(async () => await iterator.MoveNextAsync());
        Assert.Equal("PROVIDER_HTTP_500", error.ReasonCode);
        Assert.Equal(1, attempts);
    }

    [Fact(Timeout = 10_000)]
    // Token đầu phải tới trong 10 giây. Provider không tự kết thúc vẫn bị hủy sau 3 lần thử.
    public async Task StreamAsync_RequiresFirstTokenWithinTenSeconds()
    {
        var (time, reliability) = Create();
        var tokens = new List<CancellationToken>();

        async IAsyncEnumerable<ChatChunk> Operation([EnumeratorCancellation] CancellationToken token)
        {
            tokens.Add(token);
            await new TaskCompletionSource().Task;
            yield return new TextChatChunk("late");
        }

        var iterator = reliability.StreamAsync("deepseek", Operation, CancellationToken.None).GetAsyncEnumerator();
        var firstMove = iterator.MoveNextAsync().AsTask();
        await Task.Delay(50);
        time.Advance(TimeSpan.FromMilliseconds(9_900));
        await Task.Delay(100);
        Assert.Single(tokens);
        Assert.False(tokens[0].IsCancellationRequested);
        await FakeTimeDriver.AdvanceUntilCompletedAsync(time, firstMove, TimeSpan.FromMilliseconds(500));

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => firstMove);
        Assert.Equal("PROVIDER_TIMEOUT", error.ReasonCode);
        Assert.Equal(3, tokens.Count);
        Assert.All(tokens, token => Assert.True(token.IsCancellationRequested));
    }

    [Fact(Timeout = 10_000)]
    // Sau token đầu, provider im quá 15 giây thì stream bị hủy và không thử lại.
    public async Task StreamAsync_IdleProviderTimesOutAfterFifteenSeconds()
    {
        var (time, reliability) = Create();
        var tokens = new List<CancellationToken>();

        async IAsyncEnumerable<ChatChunk> Operation([EnumeratorCancellation] CancellationToken token)
        {
            tokens.Add(token);
            await Task.Yield();
            yield return new TextChatChunk("partial");
            await new TaskCompletionSource().Task;
        }

        await using var iterator = reliability.StreamAsync("deepseek", Operation, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        var secondMove = iterator.MoveNextAsync().AsTask();
        await Task.Delay(50);
        time.Advance(TimeSpan.FromMilliseconds(14_900));
        await Task.Delay(100);
        Assert.False(secondMove.IsCompleted);
        await FakeTimeDriver.AdvanceUntilCompletedAsync(time, secondMove, TimeSpan.FromMilliseconds(50));

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => secondMove);
        Assert.Equal("PROVIDER_TIMEOUT", error.ReasonCode);
        Assert.Single(tokens);
        Assert.True(tokens[0].IsCancellationRequested);
    }

    [Fact(Timeout = 10_000)]
    // Người đọc ngừng đọc 30 giây thì probe được nhả. Request khác dùng lại được provider.
    public async Task StreamAsync_ReleasesProbeWhenConsumerStopsReading()
    {
        var (time, reliability) = Create();
        RecordFailures(reliability, 5);
        time.Advance(TimeSpan.FromSeconds(60));

        async IAsyncEnumerable<ChatChunk> Operation([EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            yield return new TextChatChunk("partial");
        }

        var iterator = reliability.StreamAsync("deepseek", Operation, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal("recovered", await reliability.RunAsync("deepseek", TimeSpan.FromSeconds(30), _ => Task.FromResult("recovered"), CancellationToken.None));
        await iterator.DisposeAsync();
    }

    [Fact(Timeout = 10_000)]
    // Stream khỏe kéo dài 50 giây không bị cắt. Stream đó cũng không làm mở mạch.
    public async Task StreamAsync_LongHealthyStreamCompletes()
    {
        var (time, reliability) = Create();
        RecordFailures(reliability, 4);

        async IAsyncEnumerable<ChatChunk> Operation([EnumeratorCancellation] CancellationToken token)
        {
            for (var index = 0; index < 10; index++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), time, token);
                yield return new TextChatChunk(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            yield return new FinishChatChunk(Usage);
        }

        async Task<string> CollectAsync()
        {
            var text = string.Empty;
            await foreach (var chunk in reliability.StreamAsync("deepseek", Operation, CancellationToken.None))
            {
                if (chunk is TextChatChunk textChunk)
                {
                    text += textChunk.Text;
                }
            }

            return text;
        }

        var collected = CollectAsync();
        await FakeTimeDriver.AdvanceUntilCompletedAsync(time, collected, TimeSpan.FromMilliseconds(500));

        Assert.Equal("0123456789", await collected);
        Assert.Equal("healthy", await reliability.RunAsync("deepseek", TimeSpan.FromSeconds(30), _ => Task.FromResult("healthy"), CancellationToken.None));
    }

    [Fact(Timeout = 10_000)]
    // Stream đóng mà thiếu phần kết thúc có usage là lỗi. Gateway không ghi chi phí bằng 0 cho stream dang dở.
    public async Task StreamAsync_StreamWithoutFinishIsIncomplete()
    {
        var (time, reliability) = Create();

        async IAsyncEnumerable<ChatChunk> Operation([EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            yield return new TextChatChunk("thiếu kết thúc");
        }

        async Task DrainAsync()
        {
            await foreach (var _ in reliability.StreamAsync("deepseek", Operation, CancellationToken.None))
            {
            }
        }

        var drain = DrainAsync();
        await FakeTimeDriver.AdvanceUntilCompletedAsync(time, drain, TimeSpan.FromMilliseconds(500));

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => drain);
        Assert.Equal("PROVIDER_INCOMPLETE_STREAM", error.ReasonCode);
    }
}
