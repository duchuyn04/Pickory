using System.Text.Json.Nodes;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.AiGateway.Contracts.Calls;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Providers.DeepSeek;
using Pickory.Modules.AiGateway.Providers.OpenAi;
using Pickory.Modules.AiGateway.Settings;
using Xunit;

namespace Pickory.IntegrationTests.Database.AiGateway;

// Port từ `providers/live.int.test.ts` của bản Node, gồm 4 test gọi DeepSeek và OpenAI thật qua gateway.
// Bật bằng `AI_LIVE_TESTS=1` cùng khóa và model thật trong `.env` hoặc biến môi trường.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class LiveProviderTests(IntegrationDatabaseFixture database)
{
    private static readonly CallContext LiveContext = new(AiFeature.SmartSearchParseQuery, "user:live-test");

    private static string RequireSetting(string name, string? defaultValue = null)
    {
        return PickoryWebApplicationFactory.ResolveEnvironmentValue(name)
            ?? defaultValue
            ?? throw new InvalidOperationException($"Thiếu {name} cho test với API thật.");
    }

    private Task<GatewayHarness> CreateLiveHarnessAsync()
    {
        var chat = new DeepSeekChatProvider(
            RequireSetting("LLM_API_KEY"),
            RequireSetting("LLM_BASE_URL", "https://api.deepseek.com"),
            RequireSetting("LLM_MODEL"),
            new HttpClient());
        var embedding = new OpenAiEmbeddingProvider(
            RequireSetting("EMBEDDING_API_KEY"),
            RequireSetting("EMBEDDING_MODEL", "text-embedding-3-small"),
            new HttpClient());
        return GatewayHarness.CreateAsync(database, new AiSettings(30, 50, 15), chat, embedding);
    }

    [LiveAiFact]
    // DeepSeek trả text và usage thật. Gateway ghi một dòng log có token.
    public async Task Chat_ReturnsTextAndUsage()
    {
        var harness = await CreateLiveHarnessAsync();

        var result = await harness.Gateway.ChatAsync(
            new ChatRequest { Messages = [new UserChatMessage("Trả lời đúng một từ: xin chào")], MaxTokens = 20 },
            LiveContext);

        Assert.False(string.IsNullOrWhiteSpace(result.Text));
        Assert.True(result.Usage.InputTokens > 0);
        Assert.Equal("ok", Assert.Single(await harness.ReadLogsAsync()).Status);
    }

    [LiveAiFact]
    // DeepSeek ở chế độ JSON trả object đọc được thành kiểu C#.
    public async Task ChatJson_ReturnsTypedObject()
    {
        var harness = await CreateLiveHarnessAsync();

        var product = await harness.Gateway.ChatJsonAsync<LiveProduct>(
            new ChatRequest { Messages = [new UserChatMessage("Trả về JSON có trường name là \"Áo thun\" và price là 150000.")] },
            LiveContext);

        Assert.False(string.IsNullOrWhiteSpace(product.Name));
        Assert.True(product.Price > 0);
    }

    [LiveAiFact]
    // DeepSeek gọi tool qua ranh giới gateway, nhận kết quả rồi trả lời.
    public async Task ToolLoop_CallsToolThroughGateway()
    {
        var harness = await CreateLiveHarnessAsync();
        var calls = 0;
        var lookup = new ToolDefinition<LiveStockInput>("kiem_tra_ton_kho", "Kiểm tra số lượng tồn kho của một sản phẩm.", (_, _) =>
        {
            calls++;
            return Task.FromResult<object?>(new JsonObject { ["soLuong"] = 7 });
        });

        var result = await harness.Gateway.ChatAsync(
            new ChatRequest
            {
                Messages = [new UserChatMessage("Dùng công cụ để kiểm tra tồn kho của sản phẩm \"Áo thun\", rồi trả lời số lượng.")],
                Tools = [lookup],
                MaxSteps = 3,
            },
            LiveContext);

        Assert.True(calls >= 1);
        Assert.Contains("7", result.Text);
    }

    [LiveAiFact]
    // OpenAI trả vector 1536 chiều với tọa độ hữu hạn.
    public async Task Embed_ReturnsFiniteVector()
    {
        var harness = await CreateLiveHarnessAsync();

        var vectors = await harness.Gateway.EmbedAsync(["Laptop gaming RTX 4070"], new CallContext(AiFeature.SmartSearchIndex, "user:live-test"));

        var vector = Assert.Single(vectors);
        Assert.Equal(1536, vector.Length);
        Assert.All(vector, coordinate => Assert.True(float.IsFinite(coordinate)));
    }

    private sealed record LiveProduct(string Name, decimal Price);

    private sealed record LiveStockInput(string TenSanPham);
}
