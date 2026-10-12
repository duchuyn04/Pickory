using System.Security.Cryptography;
using System.Text;

namespace Pickory.Modules.Commerce.Payments;

internal static class SepayWebhookAuthorization
{
    public static bool Matches(string expectedKey, string? authorization)
    {
        // Khóa chưa cấu hình không được mở quyền nhận webhook.
        if (string.IsNullOrEmpty(expectedKey) || authorization is null)
        {
            return false;
        }
        // Chỉ một dấu cách sau tiền tố được phép, tránh diễn giải khác nhau giữa các lớp HTTP.
        if (!authorization.StartsWith("Apikey ", StringComparison.Ordinal))
        {
            return false;
        }
        var providedKey = authorization.AsSpan(7);
        // Khóa rỗng hoặc chứa khoảng trắng không thuộc dạng header SePay đã chốt.
        if (providedKey.IsEmpty || providedKey.ContainsAny(' ', '\t', '\r') || providedKey.Contains('\n'))
        {
            return false;
        }
        // Hai hash cùng độ dài giữ phép so thời gian cố định, kể cả khi độ dài khóa khác nhau.
        Span<byte> expectedHash = stackalloc byte[32];
        Span<byte> providedHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey), expectedHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(authorization).AsSpan(7), providedHash);
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
