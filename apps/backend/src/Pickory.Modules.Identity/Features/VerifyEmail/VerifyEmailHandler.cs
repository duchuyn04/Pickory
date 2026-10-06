using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.EmailTokens;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Features.VerifyEmail;

// Xác thực email bằng token trong link của thư. Link có hạn 24 giờ.
public sealed class VerifyEmailHandler(IdentityDbContext database, TimeProvider timeProvider)
{
    private static readonly EmailTokenPurpose[] AllowedPurposes = [EmailTokenPurpose.Verify];

    public async Task VerifyAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Đánh dấu token đã dùng và xác thực email cùng thành công hoặc cùng thất bại.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var emailToken = await EmailTokenConsumer.ConsumeAsync(
            database,
            request.Token!,
            AllowedPurposes,
            now,
            new AppException("INVALID_TOKEN", 400, "Liên kết không hợp lệ, đã hết hạn hoặc đã được sử dụng."),
            cancellationToken);

        // Prisma tự cập nhật `updatedAt` mỗi khi sửa user. Bản .NET ghi cùng cột để dữ liệu không lệch.
        await database.Users
            .Where(user => user.Id == emailToken.UserId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.EmailVerified, true)
                    .SetProperty(user => user.UpdatedAt, now),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
