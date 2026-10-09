using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.EmailTokens;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Features.ResetPassword;

// Đặt mật khẩu mới bằng token. Link quên mật khẩu và link mời nhân viên dùng chung bước này.
public sealed class ResetPasswordHandler(IdentityDbContext database, TimeProvider timeProvider)
{
    // Lời mời nhân viên dùng chung trang đặt mật khẩu. Token xác thực email không dùng được ở đây.
    private static readonly EmailTokenPurpose[] AllowedPurposes = [EmailTokenPurpose.Reset, EmailTokenPurpose.Invite];

    public async Task<UserKind> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Đổi mật khẩu, dùng token và xóa phiên nằm trong một giao dịch. Không có trạng thái làm dở.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var emailToken = await EmailTokenConsumer.ConsumeAsync(
            database,
            request.Token!,
            AllowedPurposes,
            now,
            new AppException("INVALID_TOKEN", 400, "Liên kết đặt lại mật khẩu không hợp lệ, đã hết hạn hoặc đã được sử dụng."),
            cancellationToken);
        var userId = emailToken.UserId;

        // Băm sau khi đã giữ được token. Request thua cuộc không tốn công băm Argon2.
        var passwordHash = PasswordHasher.Hash(request.NewPassword!);
        // Bấm link trong thư chứng tỏ người dùng sở hữu hòm thư. Lệnh cũng ghi `UpdatedAt` như Prisma ở bản Node.
        await database.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.PasswordHash, passwordHash)
                    .SetProperty(user => user.EmailVerified, true)
                    .SetProperty(user => user.UpdatedAt, now),
                cancellationToken);

        // Vô hiệu link đặt lại và lời mời khác còn hạn. Không còn link cũ nào dùng lại được.
        await database.EmailTokens
            .Where(token => token.UserId == userId && token.Id != emailToken.Id && token.UsedAt == null && AllowedPurposes.Contains(token.Purpose))
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.UsedAt, now), cancellationToken);

        // Mật khẩu có thể bị đổi vì nghi bị lộ. Xóa mọi phiên để người đang dùng mật khẩu cũ bị đăng xuất.
        await database.Sessions.Where(session => session.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return emailToken.User!.Kind;
    }
}
