using System.Collections.Concurrent;
using Pickory.Modules.AiGateway.Contracts.Chat;

namespace Pickory.Modules.AiGateway.Providers.Fake;

// Provider embedding giả. Model `fake-embedding` có vùng cache riêng, nên vector giả không ghi đè cache của model thật.
public sealed class FakeEmbeddingProvider(string name = "fake", string model = "fake-embedding") : IEmbeddingProvider
{
    private const int Dimensions = 1536;

    private readonly ConcurrentQueue<Func<IReadOnlyList<string>, CancellationToken, Task<EmbeddingResult>>> _scriptedResults = new();
    private readonly List<IReadOnlyList<string>> _recordedRequests = [];
    // Request song song cùng ghi danh sách. Khóa giữ danh sách đầy đủ cho test kiểm tra.
    private readonly Lock _gate = new();

    public string Name { get; } = name;
    public string Model { get; } = model;

    public IReadOnlyList<IReadOnlyList<string>> RecordedRequests
    {
        get
        {
            lock (_gate)
            {
                return _recordedRequests.ToList();
            }
        }
    }

    public void EnqueueResponse(EmbeddingResult result)
    {
        _scriptedResults.Enqueue((_, _) => Task.FromResult(result));
    }

    public void EnqueueError(Exception error)
    {
        _scriptedResults.Enqueue((_, _) => Task.FromException<EmbeddingResult>(error));
    }

    public void EnqueueHandler(Func<IReadOnlyList<string>, CancellationToken, Task<EmbeddingResult>> handler)
    {
        _scriptedResults.Enqueue(handler);
    }

    public Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _recordedRequests.Add(texts.ToList());
        }

        if (_scriptedResults.TryDequeue(out var scripted))
        {
            return scripted(texts, cancellationToken);
        }

        // Vector chỉ phụ thuộc nội dung text và chỉ số chiều. Cùng text luôn cho cùng vector.
        var vectors = texts.Select(CreateVector).ToList();
        // Ước tính 1 token cho mỗi 4 ký tự, giống provider giả của bản Node.
        var tokens = texts.Sum(text => Math.Max(1, (int)Math.Ceiling(text.Length / 4.0)));
        return Task.FromResult(new EmbeddingResult(vectors, new TokenUsage(tokens, 0, 0)));
    }

    private static float[] CreateVector(string text)
    {
        var baseValue = text.Length % 100 / 1000.0;
        var vector = new float[Dimensions];
        for (var dimension = 0; dimension < Dimensions; dimension++)
        {
            vector[dimension] = (float)Math.Round(baseValue + dimension % 10 * 0.01, 6);
        }

        return vector;
    }
}
