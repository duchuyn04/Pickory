using System.Text.Json.Nodes;

namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Tool mà model được phép gọi. Module tính năng tự gắn actor vào tool qua closure.
public abstract class ToolDefinition(string name, string description)
{
    public string Name { get; } = name;
    public string Description { get; } = description;

    // Gateway sinh JSON Schema gửi cho provider từ kiểu tham số này.
    public abstract Type ParametersType { get; }

    // Gateway gọi hàm này với input thô của model. Tool tự kiểm tra input trước khi chạy.
    public abstract Task<ToolInvocationResult> InvokeAsync(JsonNode? input, CancellationToken cancellationToken);
}
