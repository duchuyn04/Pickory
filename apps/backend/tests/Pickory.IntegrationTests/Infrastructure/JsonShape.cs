using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pickory.IntegrationTests.Infrastructure;

internal enum JsonShapeKind
{
    Null,
    String,
    Number,
    Boolean,
    Object,
    Array,
}

// Hình dạng của một giá trị JSON: tập khóa và kiểu ở mọi đường dẫn, không giữ giá trị cụ thể.
internal sealed class JsonShape
{
    private static readonly JsonShape NullShape = new(JsonShapeKind.Null, null, null);
    private static readonly JsonShape StringShape = new(JsonShapeKind.String, null, null);
    private static readonly JsonShape NumberShape = new(JsonShapeKind.Number, null, null);
    private static readonly JsonShape BooleanShape = new(JsonShapeKind.Boolean, null, null);

    private JsonShape(JsonShapeKind kind, IReadOnlyDictionary<string, JsonShape>? properties, JsonShape? element)
    {
        Kind = kind;
        Properties = properties ?? new Dictionary<string, JsonShape>();
        Element = element;
    }

    public JsonShapeKind Kind { get; }

    // Chỉ có nghĩa khi `Kind` là `Object`.
    public IReadOnlyDictionary<string, JsonShape> Properties { get; }

    // Hình dạng chung của mọi phần tử mảng. `null` khi mảng fixture rỗng, nghĩa là không có gì để kiểm.
    public JsonShape? Element { get; }

    public static JsonShapeKind KindOf(JsonNode? node)
    {
        return node switch
        {
            null => JsonShapeKind.Null,
            JsonObject => JsonShapeKind.Object,
            JsonArray => JsonShapeKind.Array,
            JsonValue value => value.GetValueKind() switch
            {
                JsonValueKind.String => JsonShapeKind.String,
                JsonValueKind.Number => JsonShapeKind.Number,
                JsonValueKind.True or JsonValueKind.False => JsonShapeKind.Boolean,
                _ => JsonShapeKind.Null,
            },
            _ => JsonShapeKind.Null,
        };
    }

    // Tên kiểu bằng tiếng Việt cho mọi thông báo lỗi. Dùng chung để hai helper gọi cùng một tên.
    public static string KindName(JsonShapeKind kind)
    {
        return kind switch
        {
            JsonShapeKind.String => "chuỗi",
            JsonShapeKind.Number => "số",
            JsonShapeKind.Boolean => "true/false",
            JsonShapeKind.Object => "object",
            JsonShapeKind.Array => "mảng",
            _ => "null",
        };
    }

    public static string JoinKey(string path, string key)
    {
        return path.Length == 0 ? key : $"{path}.{key}";
    }

    // Dựng hình dạng từ fixture, gộp mọi phần tử mảng thành một hình dạng chung. Fixture tự mâu thuẫn thì ném lỗi "Fixture sai".
    public static JsonShape FromNode(JsonNode? node, string path, FixtureContext context)
    {
        switch (KindOf(node))
        {
            // Kiểu đơn giản dùng lại một hình dạng cố định. Kiểu đơn giản không có khóa con hay phần tử.
            case JsonShapeKind.String:
                return StringShape;
            case JsonShapeKind.Number:
                return NumberShape;
            case JsonShapeKind.Boolean:
                return BooleanShape;
            case JsonShapeKind.Object:
                // Object giữ hình dạng của từng khóa con để so được tập khóa và kiểu ở mọi độ sâu.
                var properties = new Dictionary<string, JsonShape>(StringComparer.Ordinal);
                foreach (var property in node!.AsObject())
                {
                    properties[property.Key] = FromNode(property.Value, JoinKey(path, property.Key), context);
                }

                return new JsonShape(JsonShapeKind.Object, properties, null);
            case JsonShapeKind.Array:
                // Mảng gộp từng phần tử vào một hình dạng chung. Mỗi phần tử response sẽ so với hình dạng này.
                var elementPath = $"{path}[]";
                JsonShape? element = null;
                foreach (var item in node!.AsArray())
                {
                    var itemShape = FromNode(item, elementPath, context);
                    element = element is null ? itemShape : Merge(element, itemShape, elementPath, context);
                }

                return new JsonShape(JsonShapeKind.Array, null, element);
            default:
                return NullShape;
        }
    }

    // Gộp hai hình dạng của hai phần tử mảng. `null` chỉ là ký tự đại diện, nên kiểu thật ở bên kia được giữ.
    private static JsonShape Merge(JsonShape first, JsonShape second, string path, FixtureContext context)
    {
        if (first.Kind == JsonShapeKind.Null)
        {
            return second;
        }

        if (second.Kind == JsonShapeKind.Null)
        {
            return first;
        }

        // Hai phần tử fixture có hai kiểu thật khác nhau là lỗi của fixture. Dừng ngay, không đoán kiểu nào đúng.
        if (first.Kind != second.Kind)
        {
            throw context.Fault(
                $"đường dẫn `{path}`",
                $"các phần tử mảng có hai kiểu khác nhau ({KindName(first.Kind)} và {KindName(second.Kind)})");
        }

        switch (first.Kind)
        {
            case JsonShapeKind.Object:
                // Lấy hợp của mọi khóa. Khóa chỉ có ở một phần tử vẫn là khóa của hình dạng chung.
                var merged = new Dictionary<string, JsonShape>(first.Properties, StringComparer.Ordinal);
                foreach (var property in second.Properties)
                {
                    merged[property.Key] = merged.TryGetValue(property.Key, out var existing)
                        ? Merge(existing, property.Value, JoinKey(path, property.Key), context)
                        : property.Value;
                }

                return new JsonShape(JsonShapeKind.Object, merged, null);
            case JsonShapeKind.Array:
                // Mảng lồng nhau gộp tiếp hình dạng phần tử của hai mảng. Mảng rỗng không đóng góp gì.
                var element = (first.Element, second.Element) switch
                {
                    (null, var other) => other,
                    (var other, null) => other,
                    var (left, right) => Merge(left!, right!, $"{path}[]", context),
                };
                return new JsonShape(JsonShapeKind.Array, null, element);
            default:
                return first;
        }
    }
}
