using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pickory.Modules.Catalog.Descriptions;

// Schema của tài liệu mô tả sản phẩm, giống schema Zod `description-schema.ts` ở frontend.
// Tài liệu chỉ có `doc`, `paragraph`, `heading` cấp 1 đến 3 và `text` với mark `bold` hoặc `italic`.
public static class DescriptionSchema
{
    // Giới hạn text bảo vệ bộ nhớ và chi phí tạo embedding cho tìm kiếm.
    public const int MaxTextLength = 20_000;

    // Giới hạn số nút giữ cây hiển thị và bản ghi JSON ở kích thước vừa phải.
    public const int MaxNodeCount = 500;

    public static DescriptionValidationResult Validate(JsonNode? document)
    {
        var errors = new List<string>();
        var counts = new DescriptionCounts();
        ValidateDocument(document, errors, counts);
        // Chỉ kiểm tra giới hạn khi cấu trúc đúng, giống `superRefine` của Zod.
        if (errors.Count > 0)
        {
            return new DescriptionValidationResult(errors);
        }

        if (counts.TextLength > MaxTextLength)
        {
            errors.Add($"Mô tả không được vượt quá {MaxTextLength} ký tự text.");
        }

        if (counts.NodeCount > MaxNodeCount)
        {
            errors.Add($"Mô tả không được vượt quá {MaxNodeCount} nút.");
        }

        return new DescriptionValidationResult(errors);
    }

    private static void ValidateDocument(JsonNode? document, List<string> errors, DescriptionCounts counts)
    {
        // Thuộc tính thừa bị từ chối ở mọi cấp. Dữ liệu lạ không được lọt vào JSON đã lưu.
        if (!IsStrictObject(document, "doc", ["type", "content"], out var doc, errors, "doc"))
        {
            return;
        }

        // ProseMirror yêu cầu `doc` có ít nhất một khối nội dung.
        if (doc["content"] is not JsonArray blocks || blocks.Count == 0)
        {
            errors.Add("doc.content: tài liệu phải có ít nhất một khối nội dung.");
            return;
        }

        counts.NodeCount = 1;
        for (var index = 0; index < blocks.Count; index++)
        {
            ValidateBlock(blocks[index], $"doc.content[{index}]", errors, counts);
        }
    }

    private static void ValidateBlock(JsonNode? block, string path, List<string> errors, DescriptionCounts counts)
    {
        var type = (block as JsonObject)?["type"] is JsonValue typeValue && typeValue.GetValueKind() == JsonValueKind.String
            ? typeValue.GetValue<string>()
            : null;
        counts.NodeCount++;
        switch (type)
        {
            case "paragraph":
                if (IsStrictObject(block, "paragraph", ["type", "content"], out var paragraph, errors, path))
                {
                    ValidateInlineContent(paragraph, path, errors, counts);
                }

                break;
            case "heading":
                if (IsStrictObject(block, "heading", ["type", "attrs", "content"], out var heading, errors, path))
                {
                    ValidateHeadingLevel(heading["attrs"], $"{path}.attrs", errors);
                    ValidateInlineContent(heading, path, errors, counts);
                }

                break;
            default:
                // Chỉ nhận khối trong danh sách trắng. Nút như `script` hoặc `image` có thể chứa mã độc.
                errors.Add($"{path}: loại khối \"{type}\" không được hỗ trợ.");
                break;
        }
    }

    // Tiêu đề chỉ có cấp 1 đến 3. Trang sản phẩm đã dùng H1 cho tên sản phẩm.
    private static void ValidateHeadingLevel(JsonNode? attrs, string path, List<string> errors)
    {
        if (attrs is not JsonObject attrsObject || attrsObject.Any(pair => pair.Key != "level"))
        {
            errors.Add($"{path}: attrs của tiêu đề chỉ có trường level.");
            return;
        }

        var levelNode = attrsObject["level"];
        var isValidLevel = levelNode is JsonValue levelValue
            && levelValue.GetValueKind() == JsonValueKind.Number
            && levelValue.GetValue<double>() is 1 or 2 or 3;
        if (!isValidLevel)
        {
            errors.Add($"{path}.level: cấp tiêu đề chỉ nhận 1, 2 hoặc 3.");
        }
    }

    private static void ValidateInlineContent(JsonObject block, string path, List<string> errors, DescriptionCounts counts)
    {
        // Khối không có nội dung là hợp lệ, ví dụ đoạn văn trống của bản nháp.
        if (!block.ContainsKey("content"))
        {
            return;
        }

        if (block["content"] is not JsonArray inlines)
        {
            errors.Add($"{path}.content: nội dung của khối phải là mảng.");
            return;
        }

        for (var index = 0; index < inlines.Count; index++)
        {
            ValidateTextNode(inlines[index], $"{path}.content[{index}]", errors, counts);
        }
    }

    private static void ValidateTextNode(JsonNode? node, string path, List<string> errors, DescriptionCounts counts)
    {
        counts.NodeCount++;
        if (!IsStrictObject(node, "text", ["type", "text", "marks"], out var textNode, errors, path))
        {
            return;
        }

        // ProseMirror cấm nút text rỗng.
        if (textNode["text"] is not JsonValue textValue
            || textValue.GetValueKind() != JsonValueKind.String
            || textValue.GetValue<string>().Length == 0)
        {
            errors.Add($"{path}.text: nút text phải có ít nhất 1 ký tự.");
            return;
        }

        counts.TextLength += textValue.GetValue<string>().Length;
        if (textNode.ContainsKey("marks"))
        {
            ValidateMarks(textNode["marks"], $"{path}.marks", errors);
        }
    }

    private static void ValidateMarks(JsonNode? marks, string path, List<string> errors)
    {
        if (marks is not JsonArray markArray)
        {
            errors.Add($"{path}: danh sách mark phải là mảng.");
            return;
        }

        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mark in markArray)
        {
            // Chỉ nhận `bold` và `italic` trong phạm vi MVP. Mark như `link` có thể chèn URL độc.
            var markType = (mark as JsonObject)?["type"] is JsonValue typeValue && typeValue.GetValueKind() == JsonValueKind.String
                ? typeValue.GetValue<string>()
                : null;
            if (markType is not ("bold" or "italic") || mark is not JsonObject markObject || markObject.Count != 1)
            {
                errors.Add($"{path}: mark chỉ nhận bold hoặc italic, không có thuộc tính khác.");
                return;
            }

            // ProseMirror cấm hai mark cùng loại trên một nút text.
            if (!seenTypes.Add(markType))
            {
                errors.Add($"{path}: danh sách mark không được chứa mark trùng lặp cùng một loại.");
                return;
            }
        }
    }

    // Kiểm tra nút là object, có đúng `type` mong đợi và không có thuộc tính ngoài danh sách cho phép.
    private static bool IsStrictObject(
        JsonNode? node,
        string expectedType,
        string[] allowedKeys,
        out JsonObject jsonObject,
        List<string> errors,
        string path)
    {
        jsonObject = node as JsonObject ?? new JsonObject();
        var type = (node as JsonObject)?["type"] is JsonValue typeValue && typeValue.GetValueKind() == JsonValueKind.String
            ? typeValue.GetValue<string>()
            : null;
        if (node is not JsonObject || type != expectedType)
        {
            errors.Add($"{path}: nút phải có type \"{expectedType}\".");
            return false;
        }

        var extraKey = jsonObject.Select(pair => pair.Key).FirstOrDefault(key => !allowedKeys.Contains(key));
        if (extraKey is not null)
        {
            errors.Add($"{path}: thuộc tính \"{extraKey}\" không được hỗ trợ.");
            return false;
        }

        return true;
    }

    // Bộ đếm cộng dồn khi duyệt cây. Giới hạn chỉ so sánh sau khi đã duyệt hết.
    private sealed class DescriptionCounts
    {
        public int NodeCount { get; set; }
        public int TextLength { get; set; }
    }
}
