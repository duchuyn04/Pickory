using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Features.ChangePassword;

// Đổi mật khẩu và đăng xuất mọi thiết bị của người dùng.
public sealed class ChangePasswordHandler(IdentityDbContext database, TimeProvider timeProvider)
{
    public async Task ChangeAsync(string userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        // `userId` lấy từ actor, không lấy từ body. Người dùng không đổi được mật khẩu của người khác.
        var user = await database.Users.SingleAsync(value => value.Id == userId, cancellationToken);

        // Chỉ tài khoản có mật khẩu mới phải nhập mật khẩu cũ. Tài khoản chỉ dùng Google không có gì để đối chiếu.
        if (user.PasswordHash is not null)
        {
            // Người ngồi nhầm máy đang đăng nhập không đổi được mật khẩu khi không biết mật khẩu cũ.
            if (string.IsNullOrEmpty(request.CurrentPassword))
            {
                throw new AppException("INVALID_CREDENTIALS", 400, "Vui lòng cung cấp mật khẩu hiện tại.");
            }

            if (!PasswordHasher.Verify(user.PasswordHash, request.CurrentPassword))
            {
                throw new AppException("INVALID_CREDENTIALS", 400, "Mật khẩu hiện tại không chính xác.");
            }
        }

        // Băm trước khi mở giao dịch. Argon2 chạy chậm, nên giao dịch không bị giữ mở trong lúc băm.
        var newPasswordHash = PasswordHasher.Hash(request.NewPassword!);

        // Đổi mật khẩu và xóa phiên trong một giao dịch. Không có lúc mật khẩu đã đổi mà phiên cũ còn sống.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        user.PasswordHash = newPasswordHash;
        // Prisma tự cập nhật `updatedAt` mỗi khi sửa user. Bản .NET ghi cùng cột để dữ liệu không lệch.
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);
        // Xóa mọi phiên, kể cả phiên hiện tại. Kẻ đang dùng mật khẩu cũ bị đăng xuất ngay.
        await database.Sessions.Where(session => session.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
