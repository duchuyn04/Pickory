using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Tokens;

namespace Pickory.Modules.Identity.EmailTokens;

// Tạo token dùng một lần cho link trong email. Hàm trả token gốc để đưa vào thư.
public static class EmailTokenIssuer
{
    // Link xác thực email có hạn 24 giờ theo spec.
    public static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(24);
    // Link đặt lại mật khẩu có hạn 30 phút. Link lọt vào tay người khác chỉ dùng được trong thời gian ngắn.
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);

    public static string Add(IdentityDbContext database, string userId, EmailTokenPurpose purpose, TimeSpan lifetime, DateTime now)
    {
        var rawToken = SecureTokens.GenerateToken();
        // DB chỉ giữ hash. `Purpose` ngăn token đặt lại mật khẩu được dùng để xác thực email và ngược lại.
        database.EmailTokens.Add(new EmailToken
        {
            TokenHash = SecureTokens.HashToken(rawToken),
            UserId = userId,
            Purpose = purpose,
            ExpiresAt = now.Add(lifetime),
        });
        return rawToken;
    }
}
