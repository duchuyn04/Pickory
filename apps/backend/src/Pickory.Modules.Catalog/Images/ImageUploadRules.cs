namespace Pickory.Modules.Catalog.Images;

// Luật tải ảnh sản phẩm theo `spec/SPEC-catalog.md`, mục "Ảnh (Cloudinary)".
public static class ImageUploadRules
{
    // Mọi ảnh sản phẩm nằm trong một thư mục. API chỉ nhận `publicId` thuộc thư mục này.
    public const string Folder = "pickory/products";

    // Cloudinary nhận chuỗi định dạng ngăn bằng dấu phẩy, không có khoảng trắng.
    public static readonly IReadOnlyList<string> AllowedFormats = ["jpg", "png", "webp"];

    public const long MaxBytes = 5 * 1024 * 1024;

    public const int MaxImagesPerProduct = 10;

    // Alt text dài hơn mức này không còn là mô tả ngắn cho trình đọc màn hình.
    public const int MaxAltLength = 200;

    public static string AllowedFormatsParameter => string.Join(",", AllowedFormats);

    // `publicId` phải nằm trong thư mục ảnh sản phẩm. Ảnh của nơi khác trong tài khoản Cloudinary không được gắn vào sản phẩm.
    public static bool IsProductPublicId(string publicId)
    {
        var prefix = Folder + "/";
        if (!publicId.StartsWith(prefix, StringComparison.Ordinal) || publicId.Length == prefix.Length)
        {
            return false;
        }

        // Chỉ nhận ký tự an toàn trong URL. Dấu `..` hoặc ký tự lạ có thể trỏ ra ngoài thư mục khi dựng URL.
        var rest = publicId[prefix.Length..];
        return !rest.Contains("..", StringComparison.Ordinal)
            && rest.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '/');
    }

    public static bool IsAllowedFormat(string format)
    {
        // Cloudinary trả `jpg` cho cả file `.jpeg`, nên so sánh theo tên định dạng chuẩn hóa.
        return AllowedFormats.Contains(format.Trim().ToLowerInvariant());
    }
}
