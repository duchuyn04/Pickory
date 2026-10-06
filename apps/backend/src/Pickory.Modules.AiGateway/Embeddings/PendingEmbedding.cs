namespace Pickory.Modules.AiGateway.Embeddings;

// Một text mà lời gọi hiện tại chịu trách nhiệm lấy vector. Lời gọi khác cùng text chờ `Source.Task`.
public sealed record PendingEmbedding(string Key, string RedactedText, TaskCompletionSource<float[]> Source);
