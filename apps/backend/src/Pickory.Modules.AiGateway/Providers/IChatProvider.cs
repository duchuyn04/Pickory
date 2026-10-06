using Pickory.Modules.AiGateway.Contracts.Chat;

namespace Pickory.Modules.AiGateway.Providers;

// Adapter chat chỉ chạy đúng một bước. Gateway tự điều khiển vòng lặp tool, retry và timeout.
public interface IChatProvider : IAiProvider
{
    Task<ChatResult> ChatAsync(ProviderChatRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<ChatChunk> StreamAsync(ProviderChatRequest request, CancellationToken cancellationToken);
}
