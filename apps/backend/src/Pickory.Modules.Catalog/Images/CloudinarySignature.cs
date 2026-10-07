using System.Security.Cryptography;
using System.Text;

namespace Pickory.Modules.Catalog.Images;

// Tính chữ ký cho yêu cầu gửi tới Cloudinary theo tài liệu "Generating authentication signatures".
public static class CloudinarySignature
{
    // Cloudinary gửi bốn tham số này kèm yêu cầu nhưng không đưa vào chuỗi ký.
    private static readonly HashSet<string> ExcludedParameters = new(StringComparer.Ordinal)
    {
        "file", "cloud_name", "resource_type", "api_key",
    };

    public static string Sign(IReadOnlyDictionary<string, string> parameters, string apiSecret)
    {
        // Sắp xếp theo tên bằng so sánh thứ tự byte, giống cách Cloudinary dựng lại chuỗi để so chữ ký.
        var pairs = parameters
            .Where(parameter => !ExcludedParameters.Contains(parameter.Key) && parameter.Value.Length > 0)
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .Select(parameter => $"{parameter.Key}={parameter.Value}");
        // Secret nối liền sau chuỗi tham số, không có ký tự ngăn cách.
        var stringToSign = string.Join("&", pairs) + apiSecret;
        // SHA-1 là thuật toán mặc định của Cloudinary khi tài khoản không bật SHA-256.
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToHexStringLower(hash);
    }
}
