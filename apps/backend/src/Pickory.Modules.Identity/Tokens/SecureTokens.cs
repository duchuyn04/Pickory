using System.Security.Cryptography;
using System.Text;

namespace Pickory.Modules.Identity.Tokens;

// Token ngẫu nhiên cho phiên và link email. Thuật toán giữ nguyên bản Node để phiên cũ trong DB vẫn dùng được.
public static class SecureTokens
{
    private const int TokenByteLength = 32;

    public static string GenerateToken()
    {
        // Bộ sinh số ngẫu nhiên của hệ điều hành không đoán trước được. 32 byte cho 256 bit entropy.
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenByteLength));
    }

    public static string HashToken(string token)
    {
        // DB chỉ giữ hash, nên DB bị lộ cũng không cho kẻ tấn công token gốc.
        // Token đã có 256 bit ngẫu nhiên, nên SHA-256 đủ an toàn mà không cần hàm băm chậm.
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public static bool SafeEqual(string left, string right)
    {
        // So sánh mọi byte với thời gian cố định. Kẻ tấn công không đo được vị trí ký tự sai đầu tiên.
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }
}
