using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Features.DeleteAccount;

// Xóa tài khoản khách bằng cách ẩn danh dữ liệu cá nhân. Dòng user còn lại để đơn hàng cũ vẫn tham chiếu đúng.
public sealed class DeleteAccountHandler(IdentityDbContext database, IEventDispatcher eventDispatcher, TimeProvider timeProvider)
{
    // Tài khoản chỉ dùng Google phải đăng nhập lại trong khoảng này trước khi xóa.
    private static readonly TimeSpan RecentGoogleSignInWindow = TimeSpan.FromMinutes(5);

    public async Task DeleteAsync(string userId, string? password, DateTime? sessionCreatedAt, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = await database.Users
            .Include(value => value.OAuthAccounts)
            .SingleOrDefaultAsync(value => value.Id == userId, cancellationToken);

        // Tài khoản không tồn tại, bị khóa hoặc đã xóa thì không xóa tiếp.
        if (user is null || user.LockedAt is not null || user.DeletedAt is not null)
        {
            throw new AppException("USER_NOT_FOUND", 404, "Tài khoản không tồn tại hoặc đã bị khóa.");
        }

        // Admin quản lý nhân viên bằng cách khóa, không xóa. Luồng tự xóa chỉ dành cho khách.
        if (user.Kind != UserKind.Customer)
        {
            throw new AppException("FORBIDDEN", 403, "Chỉ tài khoản khách hàng mới có thể xóa qua chức năng này.");
        }

        // Xóa không đảo ngược được. Người dùng phải xác nhận lại danh tính phòng khi phiên bị người khác dùng.
        ConfirmIdentity(user, password, sessionCreatedAt, now);

        // Email ẩn danh chứa id để cột email vẫn duy nhất. Email gốc được giải phóng.
        var anonymizedEmail = $"deleted-{user.Id}@deleted.pickory.local";

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Xóa token email, liên kết Google và phiên. Không còn cách nào đăng nhập hoặc khôi phục tài khoản này.
        await database.EmailTokens.Where(token => token.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await database.OAuthAccounts.Where(account => account.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await database.Sessions.Where(session => session.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);

        // Không xóa dòng user. Đơn hàng cũ còn khóa ngoại tới dòng này, nên API chỉ ẩn danh và đánh dấu `DeletedAt`.
        await database.Users
            .Where(value => value.Id == user.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(value => value.Email, anonymizedEmail)
                    .SetProperty(value => value.Name, "Người dùng đã xóa")
                    .SetProperty(value => value.Phone, (string?)null)
                    .SetProperty(value => value.PasswordHash, (string?)null)
                    .SetProperty(value => value.DeletedAt, now)
                    .SetProperty(value => value.UpdatedAt, now),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Phát sự kiện sau khi giao dịch thành công. Module khác xóa dữ liệu riêng của người dùng này.
        await eventDispatcher.PublishAsync(new UserDeletedEvent(user.Id), cancellationToken);
    }

    private static void ConfirmIdentity(User user, string? password, DateTime? sessionCreatedAt, DateTime now)
    {
        if (user.PasswordHash is not null)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new AppException("UNAUTHORIZED", 401, "Vui lòng nhập mật khẩu để xác nhận xóa tài khoản.");
            }

            if (!PasswordHasher.Verify(user.PasswordHash, password))
            {
                throw new AppException("INVALID_CREDENTIALS", 401, "Mật khẩu xác nhận không chính xác.");
            }

            return;
        }

        // Tài khoản chỉ dùng Google không có mật khẩu. Lần đăng nhập Google gần đây là bằng chứng chính chủ.
        if (user.OAuthAccounts.Count > 0)
        {
            // Thiếu thời điểm tạo phiên thì coi như phiên quá cũ. Mặc định là từ chối, không phải cho qua.
            var sessionAge = sessionCreatedAt is null ? TimeSpan.MaxValue : now - sessionCreatedAt.Value;
            if (sessionAge > RecentGoogleSignInWindow)
            {
                throw new AppException("REAUTH_REQUIRED", 401, "Vui lòng đăng nhập lại Google trong vòng 5 phút để xác nhận xóa tài khoản.");
            }

            return;
        }

        // Không có mật khẩu và không có liên kết Google thì không có cách xác nhận nào.
        throw new AppException("UNAUTHORIZED", 401, "Thiếu phương thức xác nhận tài khoản.");
    }
}
