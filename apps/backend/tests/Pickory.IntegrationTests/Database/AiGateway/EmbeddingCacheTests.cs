using System.Security.Cryptography;
using System.Text;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Contracts.Errors;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.Modules.AiGateway.Settings;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Port từ `embedding-cache.test.ts`, `gateway.int.test.ts` và phần embedding của `gateway-regressions.test.ts`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class EmbeddingCacheTests(IntegrationDatabaseFixture database)
{
    private static readonly CallContext IndexContext = new(AiFeature.SmartSearchIndex, "user:staff_search_admin");
    private static readonly TokenUsage Usage = new(10, 5, 0);

    private static string CacheKey(string model, string redactedText)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{model}\0{redactedText}")));
    }

    [Fact]
    // Mảng rỗng trả mảng rỗng, không gọi provider, và vẫn có 1 dòng log chi phí 0.
    public async Task EmbedAsync_EmptyInputWritesOneFreeLog()
    {
        var harness = await GatewayHarness.CreateAsync(database);

        var vectors = await harness.Gateway.EmbedAsync([], IndexContext);

        Assert.Empty(vectors);
        Assert.Empty(harness.Embedding.RecordedRequests);
        var log = Assert.Single(await harness.ReadLogsAsync());
        Assert.Equal(("ok", 0d), (log.Status, log.CostUsd));
    }

    [Fact]
    // Hai text khác PII nhưng giống nhau sau khi ẩn dùng chung một dòng cache. Khóa cache tính trên text đã ẩn.
    public async Task EmbedAsync_ReusesCacheForTextsEqualAfterRedaction()
    {
        var harness = await GatewayHarness.CreateAsync(database);

        var first = await harness.Gateway.EmbedAsync(["Tư vấn mua sắm số 0912 345 678"], IndexContext);
        var second = await harness.Gateway.EmbedAsync(["Tư vấn mua sắm số 0987 654 321"], IndexContext);

        var sent = Assert.Single(harness.Embedding.RecordedRequests);
        Assert.Equal(["Tư vấn mua sắm số [đã ẩn]"], sent);
        Assert.Equal(first[0], second[0]);
        await using var aiGateway = database.CreateAiGatewayContext();
        var row = Assert.Single(aiGateway.EmbeddingCache);
        Assert.Equal(CacheKey("fake-embedding", "Tư vấn mua sắm số [đã ẩn]"), row.Key);
    }

    [Fact]
    // Gọi 2 lần cùng text chỉ gửi 1 request. Lần trúng cache vẫn có log với chi phí 0, thứ tự vector giữ nguyên.
    public async Task EmbedAsync_SecondCallUsesCache()
    {
        var harness = await GatewayHarness.CreateAsync(database);

        var first = await harness.Gateway.EmbedAsync(["sản phẩm A", "sản phẩm B"], IndexContext);
        var second = await harness.Gateway.EmbedAsync(["sản phẩm A", "sản phẩm B"], IndexContext);

        Assert.Equal(1536, first[0].Length);
        Assert.Single(harness.Embedding.RecordedRequests);
        Assert.Equal(first[0], second[0]);
        Assert.Equal(first[1], second[1]);
        Assert.Equal(2, await harness.CountCacheRowsAsync());
        var logs = await harness.ReadLogsAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(("ok", 0d), (log.Status, log.CostUsd)));
    }

    [Fact]
    // Mảng trộn text cũ và mới chỉ gửi text mới. Kết quả vẫn theo đúng thứ tự input.
    public async Task EmbedAsync_SendsOnlyMissingTextsAndKeepsOrder()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        var old = await harness.Gateway.EmbedAsync(["text cũ 1"], IndexContext);

        var mixed = await harness.Gateway.EmbedAsync(["text mới 1", "text cũ 1", "text mới 2"], IndexContext);

        Assert.Equal(["text mới 1", "text mới 2"], harness.Embedding.RecordedRequests[1]);
        Assert.Equal(3, mixed.Count);
        Assert.Equal(old[0], mixed[1]);
    }

    [Fact]
    // Ngân sách bằng 0 chặn chat, nhưng embedding đã có trong cache vẫn đọc được vì không phát sinh phí.
    public async Task EmbedAsync_ZeroBudgetStillServesCachedVectors()
    {
        var harness = await GatewayHarness.CreateAsync(database, new AiSettings(0, 50, 15));
        await Assert.ThrowsAsync<AiBudgetExceededException>(() => harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Thử chat")] },
            new CallContext(AiFeature.ShoppingAssistantChat, "user:member_zero_budget")));
        const string cachedText = "Văn bản đã nạp sẵn trong cache";
        var cachedVector = Enumerable.Repeat(0.123, 1536).ToArray();
        await harness.AddCacheRowAsync(CacheKey("fake-embedding", cachedText), cachedVector);

        var vectors = await harness.Gateway.EmbedAsync([cachedText], IndexContext);

        Assert.Equal(cachedVector.Select(value => (float)value), vectors[0]);
        Assert.Empty(harness.Embedding.RecordedRequests);
    }

    [Fact]
    // Hai lời gọi chồng nhau dùng chung một request tới provider. Mỗi lời gọi nhận vector đúng thứ tự của mình.
    public async Task EmbedAsync_OverlappingCallsShareOneProviderRequest()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        var pending = new TaskCompletionSource<EmbeddingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Embedding.EnqueueHandler((_, _) => pending.Task);

        var first = harness.Gateway.EmbedAsync(["A", "B"], IndexContext);
        await Eventually.UntilAsync(() => harness.Embedding.RecordedRequests.Count == 1);
        var second = harness.Gateway.EmbedAsync(["B", "A", "B"], IndexContext);
        await Task.Delay(100);
        pending.SetResult(new EmbeddingResult([[1f, 2f], [3f, 4f]], Usage));

        Assert.Equal([[1f, 2f], [3f, 4f]], await first);
        Assert.Equal([[3f, 4f], [1f, 2f], [3f, 4f]], await second);
        Assert.Single(harness.Embedding.RecordedRequests);
        Assert.Equal(2, await harness.CountCacheRowsAsync());
        Assert.Equal(2, (await harness.ReadLogsAsync()).Count);
    }

    [Fact]
    // Ghi cache lỗi thì không trả vector chưa lưu bền vững. Lần gọi sau phục hồi được.
    public async Task EmbedAsync_CacheWriteFailureDoesNotReturnVectors()
    {
        var interceptor = new FailOnceCommandInterceptor("INSERT INTO ai_embedding_cache", "DB cache unavailable");
        var harness = await GatewayHarness.CreateAsync(
            database,
            embeddingCacheFactory: GatewayHarness.CreateFailingFactory(database, interceptor));
        harness.Embedding.EnqueueResponse(new EmbeddingResult([[1f, 2f]], Usage));
        harness.Embedding.EnqueueResponse(new EmbeddingResult([[3f, 4f]], Usage));

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.EmbedAsync(["A"], IndexContext));
        var recovered = await harness.Gateway.EmbedAsync(["A"], IndexContext);

        Assert.Equal("EMBEDDING_CACHE_WRITE_ERROR", error.ReasonCode);
        Assert.Equal([3f, 4f], recovered[0]);
        Assert.Equal(["error", "ok"], (await harness.ReadLogsAsync()).Select(log => log.Status));
        Assert.Equal(2, harness.Embedding.RecordedRequests.Count);
    }

    [Fact]
    // Đọc cache lỗi và chặn ngân sách đều ghi đúng 1 dòng log. Provider không bị gọi.
    public async Task EmbedAsync_CacheReadFailureAndBudgetBlockEachWriteOneLog()
    {
        var interceptor = new FailOnceCommandInterceptor("SELECT", "private database connection");
        var harness = await GatewayHarness.CreateAsync(
            database,
            embeddingCacheFactory: GatewayHarness.CreateFailingFactory(database, interceptor));

        var readError = await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.EmbedAsync(["A"], IndexContext));
        await harness.SetSettingsAsync(new AiSettings(0, 50, 15));
        var budgetError = await Assert.ThrowsAsync<AiBudgetExceededException>(() => harness.Gateway.EmbedAsync(["A"], IndexContext));

        Assert.Equal("EMBEDDING_CACHE_READ_ERROR", readError.ReasonCode);
        Assert.Equal("AI_BUDGET_EXCEEDED", budgetError.Code);
        Assert.Empty(harness.Embedding.RecordedRequests);
        var logs = await harness.ReadLogsAsync();
        Assert.Equal(
            [("error", "EMBEDDING_CACHE_READ_ERROR", 0d), ("blocked", "AI_BUDGET_EXCEEDED", 0d)],
            logs.Select(log => (log.Status, log.ErrorCode, log.CostUsd)));
    }

    [Fact]
    // Lỗi provider không làm kẹt nhóm lời gọi chung. Lần gọi sau với cùng text vẫn gọi được provider.
    public async Task EmbedAsync_ProviderErrorDoesNotJamSingleFlight()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        harness.Embedding.EnqueueError(new ProviderException(400));
        harness.Embedding.EnqueueResponse(new EmbeddingResult([[1f, 2f]], Usage));

        await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.EmbedAsync(["A"], IndexContext));
        var vectors = await harness.Gateway.EmbedAsync(["A"], IndexContext);

        Assert.Equal([1f, 2f], vectors[0]);
        var logs = await harness.ReadLogsAsync();
        Assert.Equal(("error", "PROVIDER_HTTP_400"), (logs[0].Status, logs[0].ErrorCode));
        Assert.Equal("ok", logs[1].Status);
    }

    [Fact]
    // Provider trả thiếu vector thì không ghi cache sai thứ tự. Gateway báo lỗi số lượng vector.
    public async Task EmbedAsync_RejectsVectorCountMismatch()
    {
        var harness = await GatewayHarness.CreateAsync(database);
        harness.Embedding.EnqueueResponse(new EmbeddingResult([[1f, 2f]], Usage));

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.EmbedAsync(["A", "B"], IndexContext));

        Assert.Equal("EMBEDDING_VECTOR_COUNT_ERROR", error.ReasonCode);
        Assert.Equal(0, await harness.CountCacheRowsAsync());
    }

    [Fact]
    // Thiếu provider embedding thì trả lỗi không khả dụng và ghi 1 log `AI_NOT_CONFIGURED`.
    public async Task EmbedAsync_WithoutProviderLogsOutage()
    {
        var harness = await GatewayHarness.CreateAsync(database, withoutProviders: true);

        await Assert.ThrowsAsync<AiUnavailableException>(() => harness.Gateway.EmbedAsync(["A"], IndexContext));

        var log = Assert.Single(await harness.ReadLogsAsync());
        Assert.Equal(("blocked", "AI_NOT_CONFIGURED"), (log.Status, log.ErrorCode));
    }
}
