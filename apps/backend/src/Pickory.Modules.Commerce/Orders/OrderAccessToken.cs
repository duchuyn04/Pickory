using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Pickory.Modules.Commerce.Orders;

// Cặp token xem đơn. `Token` chỉ trả cho khách một lần, và DB chỉ lưu `Hash`.
public sealed record OrderAccessTokenPair(string Token, string Hash);

// Token xem đơn của khách vãng lai gồm 32 byte ngẫu nhiên, mã hóa base64url. DB lưu SHA-256 dạng hex chữ thường.
public static class OrderAccessToken
{
    public const int TokenByteLength = 32;

    public static OrderAccessTokenPair Create()
    {
        // Dùng bộ sinh số mật mã. Token đoán được cho phép kẻ tấn công xem đơn của người khác.
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var token = WebEncoders.Base64UrlEncode(bytes);
        return new OrderAccessTokenPair(token, Hash(token));
    }

    // Token đã có entropy 256 bit, nên một lần SHA-256 là đủ. Không cần hàm băm chậm như với mật khẩu.
    public static string Hash(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
