using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Providers.Fake;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Settings;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Port phần stream của `tool-loop.test.ts` và `gateway-regressions.test.ts` ở bản Node.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ChatStreamTests(IntegrationDatabaseFixture database)
{
    private static readonly ChatRequest Request = new() { Messages = [new UserChatMessage("Chào bot")] };
    private static readonly CallContext SearchContext = new(AiFeature.SmartSearchParseQuery, "user:regression");
    private static readonly TokenUsage Usage = new(10, 5, 0);

    private static async Task<List<ChatChunk>> CollectAsync(IAsyncEnumerable<ChatChunk> stream)
    {
        var chunks = new List<ChatChunk>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    [Fact]
    // Gateway phát từng chunk text ngay khi nhận. Chunk cuối mang tổng usage, và bước có 1 dòng log.
    public async Task ChatStreamAsync_EmitsChunksInOrder()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        harness.Chat.EnqueueStreamChunks(
        [
            new TextChatChunk("Xin chào "),
            new TextChatChunk("bạn, "),
            new TextChatChunk("tôi là trợ lý."),
            new FinishChatChunk(new TokenUsage(10, 8, 0)),
        ]);

        var chunks = await CollectAsync(harness.Gateway.ChatStreamAsync(Request, new CallContext(AiFeature.ShoppingAssistantChat, "user:customer_vip_001")));

        Assert.Equal(["Xin chào ", "bạn, ", "tôi là trợ lý."], chunks.OfType<TextChatChunk>().Select(chunk => chunk.Text));
        Assert.Equal(new TokenUsage(10, 8, 0), Assert.IsType<FinishChatChunk>(chunks[^1]).Usage);
        Assert.Equal("ok", Assert.Single(await harness.ReadLogsAsync()).Status);
    }

    [Fact]
    // Stream chạy vòng lặp tool như chat thường. Mỗi bước có 1 dòng log.
    public async Task ChatStreamAsync_RunsToolSteps()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        harness.Chat.EnqueueStreamChunks(
        [
            new TextChatChunk("Đang tra cứu. "),
            new FinishChatChunk(Usage, [new ToolCall("tc1", "lookup", JsonNode.Parse("""{"item":"áo"}"""))]),
        ]);
        harness.Chat.EnqueueStreamChunks([new TextChatChunk("Áo còn hàng."), new FinishChatChunk(Usage)]);
        var lookup = new ToolDefinition<ItemInput>("lookup", "Tra cứu", (input, _) => Task.FromResult<object?>($"{input.Item}: còn 3"));

        var chunks = await CollectAsync(harness.Gateway.ChatStreamAsync(Request with { Tools = [lookup], MaxSteps = 2 }, SearchContext));

        Assert.Equal("Đang tra cứu. Áo còn hàng.", string.Concat(chunks.OfType<TextChatChunk>().Select(chunk => chunk.Text)));
        Assert.Equal(new TokenUsage(20, 10, 0), Assert.IsType<FinishChatChunk>(chunks[^1]).Usage);
        var toolMessage = harness.Chat.RecordedRequests[1].Messages.OfType<ToolChatMessage>().Single();
        Assert.Equal("áo: còn 3", toolMessage.Content);
        Assert.Equal(2, (await harness.ReadLogsAsync()).Count);
    }

    [Fact]
    // Stream dùng chung hạn mức chat ngày với chat và JSON.
    public async Task ChatStreamAsync_ConsumesSharedChatTurn()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(30, 50, 1));
        var chatContext = new CallContext(AiFeature.ShoppingAssistantChat, "anon:stream-limit");

        await CollectAsync(harness.Gateway.ChatStreamAsync(Request, chatContext));

        await Assert.ThrowsAsync<AiBudgetExceededException>(() => harness.Gateway.ChatAsync(Request, chatContext));
    }

    [Fact]
    // Mạch mở hoặc thiếu provider thì stream bị chặn trước provider và có 1 dòng log.
    public async Task ChatStreamAsync_OpenCircuitAndMissingProviderAreLogged()
    {
        var reliability = new ProviderReliability(TimeProvider.System, () => 0);
        for (var failure = 0; failure < 5; failure++)
        {
            reliability.Circuit.Failure("fake", reliability.Circuit.Acquire("fake"));
        }

        var openCircuit = await GatewayHarness.CreateAsync(database, reliability: reliability);
        await Assert.ThrowsAsync<AiUnavailableException>(() => CollectAsync(openCircuit.Gateway.ChatStreamAsync(Request, SearchContext)));
        Assert.Empty(openCircuit.Chat.RecordedRequests);
        var circuitLog = Assert.Single(await openCircuit.ReadLogsAsync());
        Assert.Equal(("blocked", "CIRCUIT_OPEN"), (circuitLog.Status, circuitLog.ErrorCode));

        var missingProvider = await GatewayHarness.CreateAsync(database, withoutProviders: true);
        await Assert.ThrowsAsync<AiUnavailableException>(() => CollectAsync(missingProvider.Gateway.ChatStreamAsync(Request, SearchContext)));
        var outageLog = Assert.Single(await missingProvider.ReadLogsAsync());
        Assert.Equal(("blocked", "AI_NOT_CONFIGURED"), (outageLog.Status, outageLog.ErrorCode));
    }

    [Fact]
    // Stream đứt sau text mà thiếu usage thì log dùng số byte làm chi phí ước tính. Stream đã có usage giữ số liệu provider.
    public async Task ChatStreamAsync_BrokenStreamLogsEstimatedOrProviderUsage()
    {
        var provider = new FakeChatProvider(model: "deepseek-flash");
        var harness = await GatewayHarness.CreateAsync(database, chatProvider: provider);
        provider.EnqueueStream((_, token) => BreakAfterAsync([new TextChatChunk("hello")], token));

        var first = await Assert.ThrowsAsync<AiUnavailableException>(() => CollectAsync(harness.Gateway.ChatStreamAsync(Request, SearchContext)));
        var estimated = Assert.Single(await harness.ReadLogsAsync());

        Assert.Equal("AI_UNAVAILABLE", first.Code);
        Assert.Equal(("error", "STREAM_PARTIAL_ESTIMATED_PROVIDER_HTTP_500", 5), (estimated.Status, estimated.ErrorCode, estimated.OutputTokens));
        Assert.True(estimated.CostUsd > 0);

        provider.EnqueueStream((_, token) => BreakAfterAsync([new TextChatChunk("hello"), new FinishChatChunk(Usage)], token));
        await Assert.ThrowsAsync<AiUnavailableException>(() => CollectAsync(harness.Gateway.ChatStreamAsync(Request, SearchContext)));
        var measured = (await harness.ReadLogsAsync()).Single(log => log.Id != estimated.Id);
        Assert.Equal((10, 5, "PROVIDER_HTTP_500"), (measured.InputTokens, measured.OutputTokens, measured.ErrorCode));
    }

    [Fact]
    // Người đọc dừng sau chunk đầu. Gateway vẫn ghi 1 dòng log có chi phí ước tính.
    public async Task ChatStreamAsync_CancelledAfterTextLogsEstimatedCost()
    {
        var harness = await GatewayHarness.CreateAsync(database, chatProvider: new FakeChatProvider(model: "deepseek-flash"));

        await using (var iterator = harness.Gateway.ChatStreamAsync(Request, SearchContext).GetAsyncEnumerator())
        {
            Assert.True(await iterator.MoveNextAsync());
        }

        var log = Assert.Single(await harness.ReadLogsAsync());
        Assert.Equal(("error", "STREAM_CANCELLED_PARTIAL_ESTIMATED"), (log.Status, log.ErrorCode));
        Assert.True(log.CostUsd > 0);
    }

    // Phát các chunk rồi ném lỗi 500, giống provider đứt kết nối giữa chừng.
    private static async IAsyncEnumerable<ChatChunk> BreakAfterAsync(
        IReadOnlyList<ChatChunk> chunks,
        [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }

        throw new ProviderException(500, retryable: true);
    }

    private sealed record ItemInput(string Item);

}
