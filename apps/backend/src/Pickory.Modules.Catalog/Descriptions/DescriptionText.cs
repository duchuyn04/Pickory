using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pickory.Modules.Catalog.Descriptions;

// Đổi qua lại giữa tài liệu mô tả và text thuần, giống `description-text.ts` của frontend.
public static partial class DescriptionText
{
    // Một dòng trống trở lên là ranh giới giữa hai đoạn văn.
    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex ParagraphBreak();

    // Text thuần dùng để tạo embedding. Mark bị bỏ, mỗi khối cách nhau một dòng trống.
    public static string ToPlainText(JsonNode document)
    {
        var blocks = new List<string>();
        foreach (var block in document["content"]?.AsArray() ?? [])
        {
            var text = string.Concat((block?["content"]?.AsArray() ?? []).Select(inline => inline?["text"]?.GetValue<string>())).Trim();
            // Bỏ khối trống. Text thuần không có dòng trống thừa.
            if (text.Length > 0)
            {
                blocks.Add(text);
            }
        }

        return string.Join("\n\n", blocks);
    }

    // Đổi text thường thành tài liệu, dùng cho dữ liệu seed. Kết quả luôn qua được schema.
    public static JsonObject FromPlainText(string text)
    {
        // Chuẩn hóa xuống dòng của Windows và Mac cũ, để tách đoạn đúng trên mọi nguồn dữ liệu.
        var normalized = text.Trim().Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = ParagraphBreak().Split(normalized)
            .Select(chunk => chunk.Trim())
            .Where(chunk => chunk.Length > 0)
            .Select(chunk => (JsonNode)new JsonObject
            {
                ["type"] = "paragraph",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = chunk }),
            })
            .ToArray();

        // Text rỗng vẫn cho một tài liệu hợp lệ có một đoạn văn trống.
        if (paragraphs.Length == 0)
        {
            return new JsonObject { ["type"] = "doc", ["content"] = new JsonArray(new JsonObject { ["type"] = "paragraph" }) };
        }

        var document = new JsonObject { ["type"] = "doc", ["content"] = new JsonArray(paragraphs) };
        // Text quá dài hoặc quá nhiều đoạn vượt giới hạn của schema. Báo lỗi thay vì lưu tài liệu sai.
        var validation = DescriptionSchema.Validate(document);
        if (!validation.Valid)
        {
            throw new InvalidOperationException(string.Join("; ", validation.Errors));
        }

        return document;
    }
}
