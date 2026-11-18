using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace Pickory.IntegrationTests.Infrastructure;

// Nơi một ví dụ nằm trong fixture, cộng các hàm đọc có kiểm tra. Lỗi của fixture luôn mở đầu bằng "Fixture sai".
internal sealed record FixtureContext(string FixtureFile, string? ExampleName)
{
    public static FixtureContext For(string fixtureFile, JsonObject example)
    {
        // Tên ví dụ không phải chuỗi thì không dùng được trong thông báo. Để `null` thay vì ném lỗi thô.
        var exampleName = example["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var name) ? name : null;
        return new FixtureContext(fixtureFile, exampleName);
    }

    // Tạo lỗi riêng cho fixture sai. Người đọc test đỏ biết đây không phải lỗi của route.
    public XunitException Fault(string location, string problem)
    {
        var where = ExampleName is null ? $"`{FixtureFile}`" : $"ví dụ \"{ExampleName}\" trong `{FixtureFile}`";
        return new XunitException($"Fixture sai, không phải lỗi của route: {where}, {location}: {problem}.");
    }

    public int RequireInt(JsonNode? node, string location)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var number))
        {
            return number;
        }

        throw Fault(location, "cần một số nguyên");
    }

    public string RequireString(JsonNode? node, string location)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        throw Fault(location, "cần một chuỗi");
    }

    public JsonArray RequireArray(JsonNode? node, string location)
    {
        return node as JsonArray ?? throw Fault(location, "cần một mảng");
    }

    public JsonObject RequireObject(JsonNode? node, string location)
    {
        return node as JsonObject ?? throw Fault(location, "cần một object");
    }
}
