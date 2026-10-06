using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Pickory.Modules.AiGateway.Contracts.Chat;

namespace Pickory.Modules.AiGateway.Providers.Fake;

// Provider chat giả cho test và cho môi trường dev chưa có khóa. Kết quả theo kịch bản do test xếp hàng.
public sealed class FakeChatProvider(string name = "fake", string model = "fake-chat") : IChatProvider
{
    private static readonly TokenUsage DefaultUsage = new(10, 5, 0);

    private readonly ConcurrentQueue<Func<ProviderChatRequest, CancellationToken, Task<ChatResult>>> _scriptedResults = new();
    private readonly ConcurrentQueue<Func<ProviderChatRequest, CancellationToken, IAsyncEnumerable<ChatChunk>>> _scriptedStreams = new();
    private readonly List<ProviderChatRequest> _recordedRequests = [];
    // Request song song cùng ghi danh sách. Khóa giữ danh sách đầy đủ cho test kiểm tra.
    private readonly Lock _gate = new();

    public string Name { get; } = name;
    public string Model { get; } = model;

    // Test kiểm tra nội dung đã gửi, ví dụ text đã ẩn PII.
    public IReadOnlyList<ProviderChatRequest> RecordedRequests
    {
        get
        {
            lock (_gate)
            {
                return _recordedRequests.ToList();
            }
        }
    }

    public void EnqueueResponse(ChatResult result)
    {
        _scriptedResults.Enqueue((_, _) => Task.FromResult(result));
    }

    public void EnqueueError(Exception error)
    {
        _scriptedResults.Enqueue((_, _) => Task.FromException<ChatResult>(error));
    }

    public void EnqueueHandler(Func<ProviderChatRequest, CancellationToken, Task<ChatResult>> handler)
    {
        _scriptedResults.Enqueue(handler);
    }

    public void EnqueueStreamChunks(IReadOnlyList<ChatChunk> chunks)
    {
        _scriptedStreams.Enqueue((_, cancellationToken) => ReplayAsync(chunks, cancellationToken));
    }

    public void EnqueueStream(Func<ProviderChatRequest, CancellationToken, IAsyncEnumerable<ChatChunk>> stream)
    {
        _scriptedStreams.Enqueue(stream);
    }

    public Task<ChatResult> ChatAsync(ProviderChatRequest request, CancellationToken cancellationToken)
    {
        Record(request);
        if (_scriptedResults.TryDequeue(out var scripted))
        {
            return scripted(request, cancellationToken);
        }

        // Trả lời cố định khi test không xếp kịch bản. Dev không khóa vẫn thấy luồng chat chạy.
        return Task.FromResult(new ChatResult("Xin chào từ fake AI", DefaultUsage));
    }

    public IAsyncEnumerable<ChatChunk> StreamAsync(ProviderChatRequest request, CancellationToken cancellationToken)
    {
        Record(request);
        if (_scriptedStreams.TryDequeue(out var scripted))
        {
            return scripted(request, cancellationToken);
        }

        ChatChunk[] defaultChunks =
        [
            new TextChatChunk("Xin chào "),
            new TextChatChunk("từ fake AI"),
            new FinishChatChunk(DefaultUsage),
        ];
        return ReplayAsync(defaultChunks, cancellationToken);
    }

    private void Record(ProviderChatRequest request)
    {
        lock (_gate)
        {
            _recordedRequests.Add(request);
        }
    }

    private static async IAsyncEnumerable<ChatChunk> ReplayAsync(
        IReadOnlyList<ChatChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Nhường luồng giữa các chunk, giống stream thật nhận từng phần qua mạng.
            await Task.Yield();
            yield return chunk;
        }
    }
}
