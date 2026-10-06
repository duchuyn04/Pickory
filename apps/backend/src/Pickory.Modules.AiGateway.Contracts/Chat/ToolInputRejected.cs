namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Input của model sai kiểu hoặc sai quy tắc. `Details` có dạng `đường dẫn: lỗi` để model tự sửa ở bước sau.
public sealed record ToolInputRejected(string Details) : ToolInvocationResult;
