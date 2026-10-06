using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Tokens;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.EmailTokens;

// Dùng token trong link email đúng một lần. Gọi hàm này bên trong giao dịch của bước nghiệp vụ đi kèm.
public static class EmailTokenConsumer
{
    public static async Task<EmailToken> ConsumeAsync(
        IdentityDbContext database,
        string rawToken,
        IReadOnlyCollection<EmailTokenPurpose> allowedPurposes,
        DateTime now,
        AppException invalidTokenException,
        CancellationToken cancellationToken)
    {
        var tokenHash = SecureTokens.HashToken(rawToken);
        var emailToken = await database.EmailTokens
            .Include(value => value.User)
            .SingleOrDefaultAsync(value => value.TokenHash == tokenHash, cancellationToken);

        // Mọi lý do hỏng dùng một lỗi. Người ngoài không phân biệt được token sai, token hết hạn và token đã dùng.
        if (emailToken is null || !allowedPurposes.Contains(emailToken.Purpose) || emailToken.UsedAt is not null || emailToken.ExpiresAt <= now)
        {
            throw invalidTokenException;
        }

        // Hai request đồng thời cùng đọc thấy token còn dùng được. Lệnh cập nhật có điều kiện chỉ cho một request thắng.
        var consumedRows = await database.EmailTokens
            .Where(value => value.Id == emailToken.Id && value.UsedAt == null && value.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.UsedAt, now), cancellationToken);
        if (consumedRows != 1)
        {
            throw invalidTokenException;
        }

        return emailToken;
    }
}
