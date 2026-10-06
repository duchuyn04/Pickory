namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Số token provider báo cho một bước. `CachedTokens` nằm trong `InputTokens` và có đơn giá thấp hơn.
public sealed record TokenUsage(int InputTokens, int OutputTokens, int CachedTokens)
{
    public static TokenUsage Zero { get; } = new(0, 0, 0);
}
