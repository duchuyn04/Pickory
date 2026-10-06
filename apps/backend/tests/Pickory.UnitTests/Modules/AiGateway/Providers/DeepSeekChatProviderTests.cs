using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Providers;
using Pickory.Modules.AiGateway.Providers.DeepSeek;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Providers;

// Port từ `providers/deepseek.test.ts` của bản Node. Test dùng HTTP giả, không gọi DeepSeek thật.
public sealed class DeepSeekChatProviderTests
{
    private const string ApiKey = "secret_deepseek_token";
    private const string BaseUrl = "https://mock.deepseek.com/v1";

    private static (DeepSeekChatProvider Provider, StubHttpMessageHandler Http) Create(string model = "deepseek-flash")
    {
        var http = new StubHttpMessageHandler();
        return (new DeepSeekChatProvider(ApiKey, BaseUrl, model, new HttpClient(http)), http);
    }

    private static ProviderChatRequest Request(string content, bool json = false, IReadOnlyList<ToolDefinition>? tools = null)
    {
        return new ProviderChatRequest([new UserChatMessage(content)], tools, null, null, json);
    }

    private static string Completion(string messageJson, string usageJson = """{"prompt_tokens":18,"completion_tokens":9,"total_tokens":27}""")
    {
        return $$"""{"id":"chatcmpl-test","object":"chat.completion","created":1,"model":"deepseek-flash","choices":[{"index":0,"message":{{messageJson}},"finish_reason":"stop"}],"usage":{{usageJson}}}""";
    }

    private static async Task<List<ChatChunk>> CollectAsync(IAsyncEnumerable<ChatChunk> stream)
    {
        var chunks = new List<ChatChunk>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    // Không có model mặc định. Model rỗng là lỗi cấu hình, không thử lại được.
    public void Constructor_RejectsBlankModel(string model)
    {
        var error = Assert.Throws<ProviderException>(() => new DeepSeekChatProvider(ApiKey, BaseUrl, model, new HttpClient()));

        Assert.Equal((400, false), (error.Status, error.Retryable));
    }

    [Fact]
    // Request dùng đúng URL gốc, khóa và model. Usage đọc cả token cache hit riêng của DeepSeek.
    public async Task ChatAsync_SendsConfiguredRequestAndReadsUsage()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.OK, Completion(
            """{"role":"assistant","content":"Phản hồi từ DeepSeek"}""",
            """{"prompt_tokens":18,"completion_tokens":9,"total_tokens":27,"prompt_cache_hit_tokens":6}"""));

        var result = await provider.ChatAsync(Request("Xin chào Pickory"), CancellationToken.None);

        var request = Assert.Single(http.Requests);
        Assert.Equal("https://mock.deepseek.com/v1/chat/completions", request.Url);
        Assert.Equal($"Bearer {ApiKey}", request.Authorization);
        var body = JsonNode.Parse(request.Body)!;
        Assert.Equal("deepseek-flash", body["model"]!.GetValue<string>());
        // Tắt chế độ thinking để không mất `reasoning_content` giữa các bước tool.
        Assert.Equal("disabled", body["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal("Phản hồi từ DeepSeek", result.Text);
        Assert.Equal(new TokenUsage(18, 9, 6), result.Usage);
    }

    [Fact]
    // `MaxTokens` gửi bằng trường `max_tokens` của DeepSeek. `Temperature` gửi nguyên giá trị.
    public async Task ChatAsync_SendsMaxTokensAndTemperature()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.OK, Completion("""{"role":"assistant","content":"ok"}"""));

        await provider.ChatAsync(new ProviderChatRequest([new UserChatMessage("hi")], null, 0.2, 128, false), CancellationToken.None);

        var body = JsonNode.Parse(Assert.Single(http.Requests).Body)!;
        Assert.Equal(128, body["max_tokens"]!.GetValue<int>());
        Assert.Equal(0.2, body["temperature"]!.GetValue<double>(), precision: 5);
        Assert.Null(body["max_completion_tokens"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    // SDK không tự thử lại. Lỗi 500 và 429 chỉ gửi 1 request, gateway quyết định thử lại.
    public async Task ChatAsync_DoesNotRetryInsideSdk(HttpStatusCode status, int expectedStatus)
    {
        var (provider, http) = Create();
        http.EnqueueJson(status, """{"error":{"message":"Overloaded"}}""");
        http.EnqueueJson(status, """{"error":{"message":"Overloaded"}}""");

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.ChatAsync(Request("Kiểm tra retry"), CancellationToken.None));

        Assert.Equal((expectedStatus, true), (error.Status, error.Retryable));
        Assert.Single(http.Requests);
    }

    [Fact]
    // Lỗi 401 không thử lại. Exception không chứa khóa API mà provider gửi lại trong thông báo.
    public async Task ChatAsync_SanitizesAuthenticationError()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.Unauthorized, $$$"""{"error":{"message":"Invalid API Key {{{ApiKey}}} provided for endpoint"}}""");

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.ChatAsync(Request("Thử lỗi 401"), CancellationToken.None));

        Assert.Equal((401, false), (error.Status, error.Retryable));
        Assert.DoesNotContain(ApiKey, error.ToString());
    }

    [Fact]
    // Chế độ JSON gửi `response_format`. Adapter trả text thô, kể cả JSON chưa hoàn chỉnh, để gateway tự kiểm tra.
    public async Task ChatAsync_JsonModeReturnsRawText()
    {
        var (provider, http) = Create();
        const string malformedJson = """{"title": "Áo sơ mi", "price": """;
        http.EnqueueJson(HttpStatusCode.OK, Completion($$"""{"role":"assistant","content":{{JsonSerializer.Serialize(malformedJson)}}}"""));

        var result = await provider.ChatAsync(Request("Tạo sản phẩm JSON", json: true), CancellationToken.None);

        var body = JsonNode.Parse(Assert.Single(http.Requests).Body)!;
        Assert.Equal("json_object", body["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal(malformedJson, result.Text);
    }

    [Fact]
    // Mô tả tool và mô tả tham số bị ẩn PII trước khi gửi. Yêu cầu gọi tool của model được đọc lại đúng.
    public async Task ChatAsync_SendsRedactedToolsAndReadsToolCalls()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.OK, Completion(
            """{"role":"assistant","content":null,"tool_calls":[{"id":"call_search_123","type":"function","function":{"name":"timKiemSanPham","arguments":"{\"keyword\":\"laptop gaming\"}"}}]}"""));
        var tool = new ToolDefinition<SearchInput>(
            "timKiemSanPham",
            "Tìm kiếm sản phẩm cho khách hàng liên hệ qua 0912345678 hoặc dev@pickory.vn",
            (_, _) => Task.FromResult<object?>(null));

        var result = await provider.ChatAsync(Request("Tìm laptop", tools: [tool]), CancellationToken.None);

        var function = JsonNode.Parse(Assert.Single(http.Requests).Body)!["tools"]![0]!["function"]!;
        var description = function["description"]!.GetValue<string>();
        Assert.Contains("[đã ẩn]", description);
        Assert.DoesNotContain("0912345678", description);
        Assert.DoesNotContain("dev@pickory.vn", description);
        var propertyDescription = function["parameters"]!["properties"]!["keyword"]!["description"]!.GetValue<string>();
        Assert.Contains("[đã ẩn]", propertyDescription);
        Assert.DoesNotContain("0987654321", propertyDescription);
        var call = Assert.Single(result.ToolCalls!);
        Assert.Equal(("call_search_123", "timKiemSanPham"), (call.Id, call.Name));
        Assert.Equal("laptop gaming", call.Input!["keyword"]!.GetValue<string>());
    }

    [Fact]
    // Input sai kiểu không bị adapter từ chối. Gateway nhận đối số thô và báo lỗi cho model.
    public async Task ChatAsync_KeepsRawToolArguments()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.OK, Completion(
            """{"role":"assistant","content":null,"tool_calls":[{"id":"call_invalid_1","type":"function","function":{"name":"taoDonHang","arguments":"{\"amount\":\"khong_phai_so\"}"}}]}"""));
        var tool = new ToolDefinition<OrderInput>("taoDonHang", "Tạo đơn hàng", (_, _) => Task.FromResult<object?>(null));

        var result = await provider.ChatAsync(Request("Tạo đơn hàng", tools: [tool]), CancellationToken.None);

        var call = Assert.Single(result.ToolCalls!);
        Assert.Equal("khong_phai_so", call.Input!["amount"]!.GetValue<string>());
    }

    [Fact]
    // Lịch sử có yêu cầu gọi tool và kết quả tool được đổi sang định dạng OpenAI tương thích.
    public async Task ChatAsync_ConvertsToolHistory()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.OK, Completion("""{"role":"assistant","content":"Xong"}"""));
        ChatMessage[] messages =
        [
            new SystemChatMessage("Bạn là trợ lý"),
            new UserChatMessage("Tra đơn"),
            new AssistantChatMessage(string.Empty, [new ToolCall("call_1", "lookup", JsonNode.Parse("""{"id":5}"""))]),
            new ToolChatMessage("call_1", "lookup", "Đơn 5 đang giao"),
        ];

        await provider.ChatAsync(new ProviderChatRequest(messages, null, null, null, false), CancellationToken.None);

        var sent = JsonNode.Parse(Assert.Single(http.Requests).Body)!["messages"]!.AsArray();
        Assert.Equal(["system", "user", "assistant", "tool"], sent.Select(message => message!["role"]!.GetValue<string>()));
        var toolCall = sent[2]!["tool_calls"]![0]!;
        Assert.Equal(("call_1", "lookup"), (toolCall["id"]!.GetValue<string>(), toolCall["function"]!["name"]!.GetValue<string>()));
        Assert.Equal(5, JsonNode.Parse(toolCall["function"]!["arguments"]!.GetValue<string>())!["id"]!.GetValue<int>());
        Assert.Equal("call_1", sent[3]!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("Đơn 5 đang giao", sent[3]!["content"]!.GetValue<string>());
    }

    [Fact]
    // Stream phát từng phần text, rồi kết thúc bằng phần có usage của provider.
    public async Task StreamAsync_EmitsTextThenFinishWithUsage()
    {
        var (provider, http) = Create();
        http.EnqueueEventStream(
            """{"id":"s","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"role":"assistant","content":"Xin "},"finish_reason":null}]}""",
            """{"id":"s","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"content":"chào "},"finish_reason":null}]}""",
            """{"id":"s","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"content":"bạn!"},"finish_reason":null}]}""",
            """{"id":"s","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":6,"total_tokens":18,"prompt_cache_hit_tokens":4}}""",
            "[DONE]");

        var chunks = await CollectAsync(provider.StreamAsync(Request("Chào"), CancellationToken.None));

        Assert.Equal(["Xin ", "chào ", "bạn!"], chunks.OfType<TextChatChunk>().Select(chunk => chunk.Text));
        Assert.Equal(new TokenUsage(12, 6, 4), Assert.IsType<FinishChatChunk>(chunks[^1]).Usage);
    }

    [Fact]
    // Stream kết thúc mà thiếu usage là lỗi. Gateway không được ghi chi phí bằng 0.
    public async Task StreamAsync_RejectsMissingUsage()
    {
        var (provider, http) = Create();
        http.EnqueueEventStream(
            """{"id":"s","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"content":"Hi"},"finish_reason":null}]}""",
            """{"id":"s","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]");

        var error = await Assert.ThrowsAsync<ProviderException>(() => CollectAsync(provider.StreamAsync(Request("Test thiếu usage"), CancellationToken.None)));

        Assert.Equal(((int?)null, false), (error.Status, error.Retryable));
    }

    [Fact]
    // Lỗi HTTP khi mở stream được chuẩn hóa thành `ProviderException`.
    public async Task StreamAsync_MapsHttpError()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.TooManyRequests, """{"error":{"message":"Stream rejected"}}""");

        var error = await Assert.ThrowsAsync<ProviderException>(() => CollectAsync(provider.StreamAsync(Request("Thử lỗi stream"), CancellationToken.None)));

        Assert.Equal((429, true), (error.Status, error.Retryable));
    }

    [Fact]
    // Lỗi mạng chưa có mã HTTP được thử lại. Gateway coi đây là sự cố tạm thời.
    public async Task ChatAsync_NetworkErrorIsRetryable()
    {
        var (provider, http) = Create();
        http.EnqueueException(new HttpRequestException("connect ECONNREFUSED"));

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.ChatAsync(Request("hi"), CancellationToken.None));

        Assert.Equal(((int?)null, true, "PROVIDER_NETWORK_ERROR"), (error.Status, error.Retryable, error.Code));
    }

    private sealed class SearchInput
    {
        [Description("Từ khóa tìm kiếm hotline 0987654321")]
        public required string Keyword { get; init; }
    }

    private sealed record OrderInput(decimal Amount);
}
