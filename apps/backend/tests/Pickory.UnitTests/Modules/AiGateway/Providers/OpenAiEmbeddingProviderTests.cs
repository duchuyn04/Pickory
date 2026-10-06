using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Providers.OpenAi;
using Pickory.Modules.AiGateway.Resilience;
using Pickory.UnitTests.TestDoubles;
using Xunit;

namespace Pickory.UnitTests.Modules.AiGateway.Providers;

// Port từ `providers/openai-embedding.test.ts` của bản Node. Test dùng HTTP giả, không gọi OpenAI thật.
public sealed class OpenAiEmbeddingProviderTests
{
    private const string ApiKey = "secret_openai_token";

    private static (OpenAiEmbeddingProvider Provider, StubHttpMessageHandler Http) Create()
    {
        var http = new StubHttpMessageHandler();
        return (new OpenAiEmbeddingProvider(ApiKey, "text-embedding-3-small", new HttpClient(http)), http);
    }

    private static string EmbeddingResponse(params IEnumerable<object>[] vectors)
    {
        var data = vectors.Select((vector, index) => new { @object = "embedding", index, embedding = vector });
        return JsonSerializer.Serialize(new
        {
            @object = "list",
            data,
            model = "text-embedding-3-small",
            usage = new { prompt_tokens = 15, total_tokens = 15 },
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    // Model embedding rỗng là lỗi cấu hình, không thử lại được.
    public void Constructor_RejectsBlankModel(string model)
    {
        var error = Assert.Throws<ProviderException>(() => new OpenAiEmbeddingProvider(ApiKey, model, new HttpClient()));

        Assert.Equal((400, false), (error.Status, error.Retryable));
    }

    [Fact]
    // Request dùng đúng endpoint, khóa, model và yêu cầu 1536 chiều để khớp cột vector.
    public async Task EmbedAsync_SendsConfiguredRequest()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.OK, EmbeddingResponse(Enumerable.Repeat<object>(0.0123, 1536)));

        var result = await provider.EmbedAsync(["Văn bản cần sinh vector"], CancellationToken.None);

        var request = Assert.Single(http.Requests);
        Assert.Equal("https://api.openai.com/v1/embeddings", request.Url);
        Assert.Equal($"Bearer {ApiKey}", request.Authorization);
        var body = JsonNode.Parse(request.Body)!;
        Assert.Equal("text-embedding-3-small", body["model"]!.GetValue<string>());
        Assert.Equal(1536, body["dimensions"]!.GetValue<int>());
        Assert.Equal(1536, Assert.Single(result.Vectors).Length);
        Assert.Equal(new TokenUsage(15, 0, 0), result.Usage);
    }

    [Fact]
    // Mảng rỗng trả ngay, không gửi HTTP.
    public async Task EmbedAsync_EmptyInputSkipsHttp()
    {
        var (provider, http) = Create();

        var result = await provider.EmbedAsync([], CancellationToken.None);

        Assert.Empty(result.Vectors);
        Assert.Equal(TokenUsage.Zero, result.Usage);
        Assert.Empty(http.Requests);
    }

    [Fact]
    // Vector không đủ 1536 chiều không khớp cột vector, nên bị từ chối và không thử lại.
    public async Task EmbedAsync_RejectsWrongDimensions()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.OK, EmbeddingResponse(Enumerable.Repeat<object>(0.5, 512)));

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.EmbedAsync(["Đoạn text kiểm tra kích thước"], CancellationToken.None));

        Assert.False(error.Retryable);
    }

    [Fact]
    // Tọa độ không phải số hữu hạn bị từ chối. Vector sai không được ghi vào cache.
    public async Task EmbedAsync_RejectsNonNumericCoordinates()
    {
        var (provider, http) = Create();
        var vector = Enumerable.Repeat<object>(0.1, 1536).ToArray();
        vector[10] = "not_a_number";
        http.EnqueueJson(HttpStatusCode.OK, EmbeddingResponse(vector));

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.EmbedAsync(["Đoạn text kiểm tra tọa độ"], CancellationToken.None));

        Assert.False(error.Retryable);
    }

    [Fact]
    // Số vector khác số text thì không biết vector nào thuộc text nào, nên phản hồi bị từ chối.
    public async Task EmbedAsync_RejectsVectorCountMismatch()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.OK, EmbeddingResponse(Enumerable.Repeat<object>(0.05, 1536)));

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.EmbedAsync(["Văn bản thứ nhất", "Văn bản thứ hai"], CancellationToken.None));

        Assert.False(error.Retryable);
    }

    [Fact]
    // SDK không tự thử lại. Lỗi 500 chỉ gửi 1 request.
    public async Task EmbedAsync_DoesNotRetryInsideSdk()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.InternalServerError, """{"error":{"message":"Service unavailable"}}""");
        http.EnqueueJson(HttpStatusCode.InternalServerError, """{"error":{"message":"Service unavailable"}}""");

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.EmbedAsync(["Văn bản test retry"], CancellationToken.None));

        Assert.Equal((500, true), (error.Status, error.Retryable));
        Assert.Single(http.Requests);
    }

    [Fact]
    // Lỗi 401 không thử lại. Exception không chứa khóa API.
    public async Task EmbedAsync_SanitizesAuthenticationError()
    {
        var (provider, http) = Create();
        http.EnqueueJson(HttpStatusCode.Unauthorized, $$$"""{"error":{"message":"Incorrect API key {{{ApiKey}}} provided"}}""");

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.EmbedAsync(["Thử lỗi xác thực"], CancellationToken.None));

        Assert.Equal((401, false), (error.Status, error.Retryable));
        Assert.DoesNotContain(ApiKey, error.ToString());
    }
}
