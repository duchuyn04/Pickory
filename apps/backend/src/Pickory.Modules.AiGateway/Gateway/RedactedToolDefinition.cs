using System.Text.Json.Nodes;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Shared.Privacy;

namespace Pickory.Modules.AiGateway.Gateway;

// Bản tool gửi cho provider. Mô tả có thể chứa PII, nên gateway ẩn PII trước khi gửi.
public sealed class RedactedToolDefinition(ToolDefinition inner)
    : ToolDefinition(inner.Name, PiiRedactor.Redact(inner.Description))
{
    public override Type ParametersType => inner.ParametersType;

    public override Task<ToolInvocationResult> InvokeAsync(JsonNode? input, CancellationToken cancellationToken)
    {
        return inner.InvokeAsync(input, cancellationToken);
    }
}
