using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Embeddings;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Modules.AiGateway.Resilience;

namespace Pickory.Modules.AiGateway.Providers.OpenAi;

// Adapter embedding cho OpenAI. Vector có 1536 chiều để khớp cột `vector(1536)` của pgvector.
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private const int Dimensions = 1536;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

    public OpenAiEmbeddingProvider(string apiKey, string model, HttpClient httpClient)
    {
        // Không có model mặc định ngầm. Model rỗng là lỗi cấu hình.
        var modelName = model.Trim();
        if (modelName.Length == 0)
        {
            throw new ProviderException(400, retryable: false);
        }

        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
            // SDK không tự thử lại. Gateway quyết định số lần gọi ra ngoài ở một chỗ.
            RetryPolicy = new ClientRetryPolicy(0),
        };
        _generator = new EmbeddingClient(modelName, new ApiKeyCredential(apiKey), options).AsIEmbeddingGenerator();
        Model = modelName;
    }

    public string Name => "openai";
    public string Model { get; }

    public async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        // Mảng rỗng không cần gọi provider.
        if (texts.Count == 0)
        {
            return new EmbeddingResult([], TokenUsage.Zero);
        }

        GeneratedEmbeddings<Embedding<float>> generated;
        try
        {
            generated = await _generator.GenerateAsync(
                texts,
                new Microsoft.Extensions.AI.EmbeddingGenerationOptions { Dimensions = Dimensions },
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderErrorMapper.Map(exception);
        }

        // Số vector phải khớp số text. Thiếu vector thì không biết vector nào thuộc text nào.
        if (generated.Count != texts.Count)
        {
            throw new ProviderException(null, retryable: false);
        }

        var vectors = new List<float[]>(generated.Count);
        foreach (var embedding in generated)
        {
            var vector = embedding.Vector.ToArray();
            // Vector sai chiều hoặc có tọa độ không hữu hạn làm hỏng tìm kiếm tương đồng sau này.
            if (vector.Length != Dimensions || vector.Any(coordinate => !float.IsFinite(coordinate)))
            {
                throw new ProviderException(null, retryable: false);
            }

            vectors.Add(vector);
        }

        return new EmbeddingResult(vectors, new TokenUsage((int)(generated.Usage?.InputTokenCount ?? 0), 0, 0));
    }
}
