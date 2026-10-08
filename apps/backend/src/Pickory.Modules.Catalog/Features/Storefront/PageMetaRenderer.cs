using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Nội dung thẻ meta của một trang. `Url` là null khi trang không có địa chỉ chuẩn, ví dụ trang không tìm thấy.
public sealed record PageMeta(
    string Title, string Description, string? Url, string OgType, string? ImageUrl, bool NoIndex);

// Chèn thẻ meta vào template `index.html` của SPA để trình thu thập và trình xem trước liên kết đọc được.
public static partial class PageMetaRenderer
{
    private const string SiteName = "Pickory";

    // Cho phép mọi ký tự Unicode ra nguyên dạng, để chữ Việt có dấu không thành thực thể số. Ký tự nguy hiểm vẫn bị escape.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    [GeneratedRegex(@"<title>.*?</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleElement();

    public static string Render(string template, PageMeta meta)
    {
        // Template sai là lỗi cấu hình triển khai, không phải lỗi của khách, nên ném lỗi rõ ràng thay vì trả trang hỏng.
        var titleMatch = TitleElement().Match(template);
        if (!titleMatch.Success)
        {
            throw new InvalidOperationException("Template index.html thiếu thẻ <title>.");
        }

        var headEnd = template.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEnd < 0)
        {
            throw new InvalidOperationException("Template index.html thiếu thẻ </head>.");
        }

        // Thay nội dung thẻ title trước. Thẻ title nằm trong head nên vị trí `</head>` đổi theo độ dài mới.
        var withTitle = TitleElement().Replace(template, _ => $"<title>{Encode(meta.Title)}</title>", 1);
        var insertAt = withTitle.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        var tags = new StringBuilder();
        AddLine(tags, $"    <meta name=\"description\" content=\"{Encode(meta.Description)}\" />");
        if (meta.Url is not null)
        {
            AddLine(tags, $"    <link rel=\"canonical\" href=\"{Encode(meta.Url)}\" />");
        }

        AddLine(tags, $"    <meta property=\"og:type\" content=\"{Encode(meta.OgType)}\" />");
        AddLine(tags, $"    <meta property=\"og:title\" content=\"{Encode(meta.Title)}\" />");
        AddLine(tags, $"    <meta property=\"og:description\" content=\"{Encode(meta.Description)}\" />");
        if (meta.Url is not null)
        {
            AddLine(tags, $"    <meta property=\"og:url\" content=\"{Encode(meta.Url)}\" />");
        }

        if (meta.ImageUrl is not null)
        {
            AddLine(tags, $"    <meta property=\"og:image\" content=\"{Encode(meta.ImageUrl)}\" />");
        }

        AddLine(tags, $"    <meta property=\"og:site_name\" content=\"{SiteName}\" />");
        if (meta.NoIndex)
        {
            AddLine(tags, "    <meta name=\"robots\" content=\"noindex\" />");
        }

        // Xuống dòng cuối để thẻ `</head>` không dính vào thẻ meta cuối cùng.
        tags.Append('\n');
        return withTitle.Insert(insertAt, tags.ToString());
    }

    // Mỗi thẻ một dòng, dùng `\n` cố định để kết quả giống nhau trên mọi hệ điều hành.
    private static void AddLine(StringBuilder tags, string line) => tags.Append('\n').Append(line);

    // Mọi giá trị vào HTML đều qua đây. Tên và mô tả do nhân viên nhập, nên không được ghép chuỗi chưa escape.
    private static string Encode(string value) => Encoder.Encode(value);
}
