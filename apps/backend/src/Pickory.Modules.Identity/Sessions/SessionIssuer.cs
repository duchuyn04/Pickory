using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Tokens;

namespace Pickory.Modules.Identity.Sessions;

// Tạo phiên mới cho một tài khoản đã xác thực.
public static class SessionIssuer
{
    public static async Task<IssuedSession> IssueAsync(
        IdentityDbContext database,
        string userId,
        TimeSpan lifetime,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Token gốc chỉ đi vào cookie một lần. DB chỉ giữ hash của token.
        var rawToken = SecureTokens.GenerateToken();
        var expiresAt = now.Add(lifetime);
        database.Sessions.Add(new Session
        {
            TokenHash = SecureTokens.HashToken(rawToken),
            UserId = userId,
            ExpiresAt = expiresAt,
        });
        await database.SaveChangesAsync(cancellationToken);
        return new IssuedSession(rawToken, expiresAt);
    }
}
