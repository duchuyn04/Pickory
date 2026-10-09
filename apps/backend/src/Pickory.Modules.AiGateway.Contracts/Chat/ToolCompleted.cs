namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Tool chạy xong. Gateway ẩn PII trong `Value` trước khi gửi lại cho model.
public sealed record ToolCompleted(object? Value) : ToolInvocationResult;
