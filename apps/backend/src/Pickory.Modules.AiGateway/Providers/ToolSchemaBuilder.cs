using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Pickory.Modules.AiGateway.Contracts.Chat;
using Pickory.Shared.Privacy;

namespace Pickory.Modules.AiGateway.Providers;

// Đổi tool của Pickory thành khai báo tool của `Microsoft.Extensions.AI`. Adapter không chạy tool.
public static class ToolSchemaBuilder
{
    // Từ khóa cấu trúc của JSON Schema giữ nguyên. Ẩn PII trong các từ khóa này sẽ làm hỏng schema.
    private static readonly HashSet<string> StructuralKeywords = new(StringComparer.Ordinal)
    {
        "type",
        "format",
        "$schema",
        "$ref",
        "additionalProperties",
    };

    public static AIFunctionDeclaration ToDeclaration(ToolDefinition tool)
    {
        // Schema sinh từ kiểu tham số theo cùng quy tắc JSON mà gateway dùng để đọc input của tool.
        var schema = AIJsonUtilities.CreateJsonSchema(tool.ParametersType, serializerOptions: AiJsonReader.Options);
        var redactedSchema = RedactNode(JsonNode.Parse(schema.GetRawText()));
        var schemaElement = JsonSerializer.SerializeToElement(redactedSchema);
        // Mô tả của tham số có thể chứa PII, giống mô tả của tool.
        return AIFunctionFactory.CreateDeclaration(tool.Name, PiiRedactor.Redact(tool.Description), schemaElement);
    }

    private static JsonNode? RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                var result = new JsonObject();
                foreach (var (key, value) in jsonObject)
                {
                    result[key] = StructuralKeywords.Contains(key) ? value?.DeepClone() : RedactNode(value);
                }

                return result;
            case JsonArray jsonArray:
                return new JsonArray(jsonArray.Select(RedactNode).ToArray());
            case JsonValue jsonValue when jsonValue.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(PiiRedactor.Redact(jsonValue.GetValue<string>()));
            default:
                return node?.DeepClone();
        }
    }
}
