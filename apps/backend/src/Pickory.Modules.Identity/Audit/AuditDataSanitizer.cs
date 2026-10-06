using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pickory.Shared.Json;

namespace Pickory.Modules.Identity.Audit;

// Giấu giá trị nhạy cảm trước khi ghi nhật ký thao tác. Nhật ký không bao giờ chứa bí mật.
public static partial class AuditDataSanitizer
{
    public const string RedactedValue = "[REDACTED]";

    public static JsonNode? Sanitize(object? value)
    {
        // Dùng cùng cấu hình JSON với API. Thời gian thành chuỗi ISO có `Z`, giống `toISOString()` của bản Node.
        var node = JsonSerializer.SerializeToNode(value, PickoryJsonDefaults.Options);
        return SanitizeNode(node);
    }

    private static JsonNode? SanitizeNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                var sanitizedObject = new JsonObject();
                foreach (var (key, child) in jsonObject)
                {
                    // Trường nhạy cảm giữ tên nhưng mất giá trị, nên người xem biết trường đó từng đổi.
                    // Trường thường được làm sạch tiếp vì bí mật có thể nằm sâu trong đối tượng lồng nhau.
                    sanitizedObject[key] = IsSensitiveKey(key) ? JsonValue.Create(RedactedValue) : SanitizeNode(child?.DeepClone());
                }

                return sanitizedObject;
            case JsonArray jsonArray:
                // Bí mật có thể nằm trong đối tượng con của mảng.
                return new JsonArray(jsonArray.Select(item => SanitizeNode(item?.DeepClone())).ToArray());
            default:
                // Số, chuỗi, giá trị logic và `null` không có tên trường để xét.
                return node?.DeepClone();
        }
    }

    // Bỏ gạch nối và gạch dưới trước khi so khớp. Tên như `api_key` và `card-number` cũng bị bắt.
    private static bool IsSensitiveKey(string key)
    {
        return SensitiveKeyPattern().IsMatch(key.Replace("-", string.Empty).Replace("_", string.Empty));
    }

    // Khớp một phần tên trường. Mẫu bắt cả `accessToken`, `newPassword` và `passwordHash`, giống bản Node.
    [GeneratedRegex("(password|token|secret|apikey|cvv|cardnumber|hash)", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKeyPattern();
}
