using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;

namespace Pickory.Modules.Identity.Passwords;

// Băm mật khẩu bằng Argon2id với tham số mặc định của `@node-rs/argon2`. Người dùng cũ không phải đặt lại mật khẩu.
public static class PasswordHasher
{
    // Hash hợp lệ không thuộc tài khoản nào. Nhánh không có tài khoản vẫn tốn thời gian băm như nhánh có tài khoản.
    public const string DummyHash = "$argon2id$v=19$m=19456,t=2,p=1$Q29uc3RhbnRUaW1lU2FsdA$uF7hN93Qx2Tq89jFp1m5A0/X84f/7Dk06u6jJ7uV6U4";

    private const int MemoryCostKibibytes = 19456;
    private const int TimeCost = 2;
    private const int Parallelism = 1;
    private const int HashLengthBytes = 32;
    private const int SaltLengthBytes = 16;

    public static string Hash(string password)
    {
        // Argon2id tốn cả CPU và RAM, nên tấn công dò mật khẩu bằng GPU rất tốn kém.
        // Salt ngẫu nhiên nằm trong chuỗi hash, nên DB không cần cột salt riêng.
        var config = new Argon2Config
        {
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            MemoryCost = MemoryCostKibibytes,
            TimeCost = TimeCost,
            Lanes = Parallelism,
            Threads = Parallelism,
            HashLength = HashLengthBytes,
            Salt = RandomNumberGenerator.GetBytes(SaltLengthBytes),
            Password = Encoding.UTF8.GetBytes(password),
        };
        return Argon2.Hash(config);
    }

    public static bool Verify(string encodedHash, string password)
    {
        try
        {
            // Thư viện đọc tham số và salt từ chính chuỗi hash, rồi băm lại để so sánh.
            return Argon2.Verify(encodedHash, password);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Hash hỏng nghĩa là mật khẩu không khớp. Lỗi nội bộ không được lộ ra cho người đăng nhập.
            return false;
        }
    }
}
