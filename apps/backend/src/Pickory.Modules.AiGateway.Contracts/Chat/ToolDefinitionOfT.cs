using System.Text.Json.Nodes;
using FluentValidation;

namespace Pickory.Modules.AiGateway.Contracts.Chat;

// Tool có tham số kiểu `TParameters`. Gateway sinh JSON Schema từ kiểu này để gửi cho provider.
public sealed class ToolDefinition<TParameters>(
    string name,
    string description,
    Func<TParameters, CancellationToken, Task<object?>> execute,
    IValidator<TParameters>? validator = null) : ToolDefinition(name, description)
{
    public override Type ParametersType => typeof(TParameters);

    public override async Task<ToolInvocationResult> InvokeAsync(JsonNode? input, CancellationToken cancellationToken)
    {
        // Model có thể sinh input sai kiểu. Tool chỉ chạy với input đã đọc và kiểm tra thành công.
        var read = AiJsonReader.Read(input, validator);
        if (!read.Succeeded)
        {
            return new ToolInputRejected(read.Details!);
        }

        return new ToolCompleted(await execute(read.Value!, cancellationToken));
    }
}
