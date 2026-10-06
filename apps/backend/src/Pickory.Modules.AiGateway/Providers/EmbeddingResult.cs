using Pickory.Modules.AiGateway.Contracts.Chat;

namespace Pickory.Modules.AiGateway.Providers;

public sealed record EmbeddingResult(IReadOnlyList<float[]> Vectors, TokenUsage Usage);
