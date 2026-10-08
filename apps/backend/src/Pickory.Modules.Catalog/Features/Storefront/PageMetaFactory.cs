using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Descriptions;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Dựng nội dung meta cho trang sản phẩm, trang danh mục và trang không tìm thấy từ dữ liệu đã có.
public static partial class PageMetaFactory
{
    public const int MaxDescriptionLength = 160;

    public const string ShopDescription = "Pickory bán đồ điện tử chính hãng: điện thoại, laptop, phụ kiện.";

    private const string TitleSuffix = " | Pickory";
    private const string UploadSegment = "/image/upload/";
    private const string OgImageTransform = "f_auto,q_auto,w_1200/";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static PageMeta ForProduct(ProductDetail product, string webOrigin)
    {
        // Mô tả ngắn thắng vì nhân viên viết riêng cho kết quả tìm kiếm. Rỗng thì lấy text thuần của mô tả dài.
        var source = string.IsNullOrWhiteSpace(product.ShortDesc)
            ? DescriptionText.ToPlainText(JsonNode.Parse(product.Description.GetRawText())!)
            : product.ShortDesc;
        var description = TruncateDescription(source);

        return new PageMeta(
            $"{product.Name}{TitleSuffix}",
            description.Length > 0 ? description : ShopDescription,
            PageUrl(webOrigin, "p", product.Slug),
            "product",
            product.Images.Count > 0 ? WithOgImageTransform(product.Images[0].Url) : null,
            NoIndex: false);
    }

    public static PageMeta ForCategory(string name, string slug, string webOrigin) =>
        new(
            $"{name}{TitleSuffix}",
            $"Mua {name} chính hãng tại Pickory.",
            PageUrl(webOrigin, "c", slug),
            "website",
            ImageUrl: null,
            NoIndex: false);

    // Slug lạ, `draft` và `hidden` dùng chung meta này. `noindex` giữ trang lỗi ngoài kết quả tìm kiếm.
    public static PageMeta NotFound() =>
        new($"Không tìm thấy{TitleSuffix}", ShopDescription, Url: null, "website", ImageUrl: null, NoIndex: true);

    // Cắt ở ranh giới từ để mô tả không bị đứt giữa chữ. Dấu ba chấm tính vào giới hạn.
    public static string TruncateDescription(string text)
    {
        var normalized = Whitespace().Replace(text, " ").Trim();
        if (normalized.Length <= MaxDescriptionLength)
        {
            return normalized;
        }

        var cut = normalized[..(MaxDescriptionLength - 1)];
        // Chữ cuối đã trọn khi ký tự kế tiếp là khoảng trắng. Ngược lại lùi về khoảng trắng gần nhất.
        var endsAtWord = normalized[MaxDescriptionLength - 1] == ' ';
        var lastSpace = cut.LastIndexOf(' ');
        if (!endsAtWord && lastSpace > 0)
        {
            cut = cut[..lastSpace];
        }

        return cut.TrimEnd() + "…";
    }

    // Đường dẫn chuẩn không kèm query string, để mọi biến thể `?sku=` cùng trỏ về một địa chỉ.
    private static string PageUrl(string webOrigin, string routePrefix, string slug) =>
        $"{webOrigin.TrimEnd('/')}/{routePrefix}/{Uri.EscapeDataString(slug)}";

    // Ảnh chia sẻ cần cỡ 1200 px. Cloudinary chỉ nhận biến đổi sau `/image/upload/`.
    private static string WithOgImageTransform(string url)
    {
        var index = url.IndexOf(UploadSegment, StringComparison.Ordinal);
        return index < 0 ? url : url.Insert(index + UploadSegment.Length, OgImageTransform);
    }
}
