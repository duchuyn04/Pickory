namespace Pickory.Modules.AiGateway.Contracts.Calls;

// Thông tin của một lời gọi AI.
// `ActorKey` có dạng `user:<id>` hoặc `anon:<id>`, còn `PromptVersion` có dạng `review-summary@3`.
public sealed record CallContext(AiFeature Feature, string? ActorKey = null, string? PromptVersion = null);
