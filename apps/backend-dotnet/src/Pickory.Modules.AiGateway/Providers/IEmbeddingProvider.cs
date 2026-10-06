namespace Pickory.Modules.AiGateway.Providers;

public interface IEmbeddingProvider : IAiProvider
{
    // Trả một vector cho mỗi text, đúng thứ tự input.
    Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
