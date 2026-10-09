namespace Pickory.Modules.AiGateway.Providers;

// Provider đang dùng. Giá trị null nghĩa là production thiếu khóa, gateway trả lỗi thay vì dùng provider giả.
public sealed record AiProviderSet(IChatProvider? Chat, IEmbeddingProvider? Embedding);
