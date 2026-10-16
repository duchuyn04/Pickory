using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.GoogleOAuth;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Tokens;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Features.GoogleSignIn;

// Hoàn tất đăng nhập Google: kiểm tra state, đổi code, rồi tạo hoặc liên kết tài khoản khách và cấp phiên.
public sealed class GoogleCallbackHandler(
    IdentityDbContext database,
    IGoogleOAuthClient googleClient,
    IEventDispatcher eventDispatcher,
    TimeProvider timeProvider)
{
    private const string GoogleProvider = "google";

    public async Task<(UserSummaryResponse User, IssuedSession Session)> SignInAsync(
        string code,
        string returnedState,
        OAuthStateCookie? savedState,
        string redirectUri,
        string anonymousId,
        CancellationToken cancellationToken)
    {
        // State trên URL phải khớp state trong cookie. So sánh thời gian cố định để thời gian phản hồi không lộ state.
        if (savedState is null || string.IsNullOrEmpty(savedState.State) || !SecureTokens.SafeEqual(returnedState, savedState.State))
        {
            throw new AppException("INVALID_OAUTH_STATE", 400, "Mã state không hợp lệ hoặc phiên đăng nhập Google đã hết hạn.");
        }

        // Mọi thông tin người dùng lấy từ Google. API không tin dữ liệu nào do trình duyệt gửi lên.
        var accessToken = await googleClient.ExchangeCodeAsync(code, savedState.CodeVerifier, redirectUri, cancellationToken);
        var userInfo = await googleClient.GetUserInfoAsync(accessToken, cancellationToken);
        // Chuẩn hóa email trước khi tra cứu. Email viết hoa và viết thường thuộc cùng một tài khoản.
        var email = UserInputNormalizer.NormalizeEmail(userInfo.Email);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existingUser = await database.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
        EnsureExistingUserMayUseGoogle(existingUser, userInfo);

        var (targetUser, emailBecameVerified) = await ResolveTargetUserAsync(existingUser, userInfo, email, now, cancellationToken);

        // Kiểm tra lần cuối ngay trước khi cấp phiên. Nhánh nào bỏ sót trạng thái khóa cũng không cấp được phiên.
        if (targetUser.LockedAt is not null || targetUser.DeletedAt is not null)
        {
            throw CreateUnavailableAccountException();
        }

        // Phiên khách có hạn 30 ngày, cùng quy tắc với đăng nhập bằng mật khẩu.
        var session = await SessionIssuer.IssueAsync(database, targetUser.Id, SessionCookies.CustomerSessionLifetime, now, cancellationToken);

        // Gộp giỏ hàng của mã ẩn danh như đăng nhập thường. Handler lỗi chỉ bị ghi log.
        await eventDispatcher.PublishAsync(new UserLinkedEvent(anonymousId, targetUser.Id), cancellationToken);

        // Email vừa được Google chứng minh. Module khác gắn đơn vãng lai cùng email vào tài khoản này.
        if (emailBecameVerified)
        {
            await eventDispatcher.PublishAsync(new EmailVerifiedEvent(targetUser.Id, targetUser.Email), cancellationToken);
        }

        return (new UserSummaryResponse(targetUser.Id, targetUser.Email, targetUser.Name, UserSummaryResponse.CustomerKind), session);
    }

    private static void EnsureExistingUserMayUseGoogle(User? existingUser, GoogleUserInfo userInfo)
    {
        if (existingUser is null)
        {
            return;
        }

        // Nhân viên chỉ đăng nhập bằng email và mật khẩu theo spec. Tài khoản quyền lớn không có thêm đường vào.
        if (existingUser.Kind == UserKind.Staff)
        {
            throw new AppException("FORBIDDEN", 403, "Tài khoản nhân viên không được phép đăng nhập bằng Google.");
        }

        // Ai cũng tạo được tài khoản Google với email của người khác. Email Google chưa xác thực không được gộp vào tài khoản có sẵn.
        if (!userInfo.EmailVerified)
        {
            throw new AppException("UNVERIFIED_EMAIL", 400, "Email Google chưa được xác thực, không thể tự động liên kết với tài khoản hiện có.");
        }

        // Kiểm tra trước khi tạo liên kết. Tài khoản bị khóa không liên kết thêm được Google.
        if (existingUser.LockedAt is not null || existingUser.DeletedAt is not null)
        {
            throw CreateUnavailableAccountException();
        }
    }

    // Trả thêm cờ cho biết `EmailVerified` có vừa đổi từ sai sang đúng trong lần đăng nhập này hay không.
    private async Task<(User User, bool EmailBecameVerified)> ResolveTargetUserAsync(
        User? existingUser,
        GoogleUserInfo userInfo,
        string email,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Ưu tiên tìm theo `sub`. Mã này cố định, còn email Google của người dùng có thể đã đổi.
        var linkedAccount = await database.OAuthAccounts
            .Include(account => account.User)
            .SingleOrDefaultAsync(account => account.Provider == GoogleProvider && account.ProviderUserId == userInfo.Sub, cancellationToken);
        if (linkedAccount?.User is { } linkedUser)
        {
            // Tài khoản đã liên kết vẫn bị chặn khi bị khóa hoặc bị xóa sau lúc liên kết.
            if (linkedUser.LockedAt is not null || linkedUser.DeletedAt is not null)
            {
                throw CreateUnavailableAccountException();
            }

            // Tài khoản đã liên kết từ trước nên không có thay đổi nào về xác thực email.
            return (linkedUser, false);
        }

        if (existingUser is not null)
        {
            var linkedNow = await LinkExistingUserAsync(existingUser, userInfo.Sub, now, cancellationToken);
            return (existingUser, linkedNow);
        }

        var createdUser = await CreateGoogleCustomerAsync(userInfo, email, now, cancellationToken);
        // Tài khoản mới chỉ có email đã xác thực khi Google xác nhận email đó.
        return (createdUser, createdUser.EmailVerified);
    }

    // Trả `true` khi tài khoản đổi từ chưa xác thực sang đã xác thực nhờ lần liên kết này.
    private async Task<bool> LinkExistingUserAsync(User existingUser, string googleSubject, DateTime now, CancellationToken cancellationToken)
    {
        // Email đã xác thực ở cả hai phía. Liên kết an toàn và mật khẩu hiện có được giữ nguyên.
        if (existingUser.EmailVerified)
        {
            database.OAuthAccounts.Add(new OAuthAccount { Provider = GoogleProvider, ProviderUserId = googleSubject, UserId = existingUser.Id });
            await database.SaveChangesAsync(cancellationToken);
            return false;
        }

        // Kẻ xấu có thể đã đăng ký trước bằng email của nạn nhân, nên API thu hồi quyền truy cập cũ.
        // Giao dịch bảo đảm mật khẩu cũ chỉ bị xóa khi liên kết Google được tạo.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Xóa mật khẩu do người đăng ký trước đặt. Chủ email dùng Google hoặc đặt lại mật khẩu qua email.
        existingUser.PasswordHash = null;
        existingUser.EmailVerified = true;
        existingUser.UpdatedAt = now;
        database.OAuthAccounts.Add(new OAuthAccount { Provider = GoogleProvider, ProviderUserId = googleSubject, UserId = existingUser.Id });
        await database.SaveChangesAsync(cancellationToken);

        // Xóa phiên mà người đăng ký trước có thể đang giữ.
        await database.Sessions.Where(session => session.UserId == existingUser.Id).ExecuteDeleteAsync(cancellationToken);
        // Vô hiệu link xác thực và link đặt lại còn hạn mà người đăng ký trước đã yêu cầu.
        await database.EmailTokens
            .Where(token => token.UserId == existingUser.Id && token.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.UsedAt, now), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<User> CreateGoogleCustomerAsync(GoogleUserInfo userInfo, string email, DateTime now, CancellationToken cancellationToken)
    {
        // Thiếu role `customer` là lỗi cấu hình do chưa seed. Lỗi này trả 500, không phải lỗi của người dùng.
        var customerRole = await database.Roles.SingleOrDefaultAsync(role => role.Code == DefaultRoles.CustomerCode, cancellationToken)
            ?? throw new InvalidOperationException("Role customer chưa được seed trong DB.");

        // Tài khoản chỉ đăng nhập bằng Google, nên không có hash mật khẩu. Cờ xác thực email lấy theo Google.
        var user = new User
        {
            Email = email,
            EmailVerified = userInfo.EmailVerified,
            Name = string.IsNullOrEmpty(userInfo.Name) ? null : userInfo.Name,
            Kind = UserKind.Customer,
            UpdatedAt = now,
        };
        user.Roles.Add(new UserRole { User = user, RoleId = customerRole.Id });
        user.OAuthAccounts.Add(new OAuthAccount { Provider = GoogleProvider, ProviderUserId = userInfo.Sub, User = user });
        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);
        return user;
    }

    // Một thông báo cho tài khoản bị khóa và tài khoản bị xóa. Người ngoài không phân biệt được hai trạng thái này.
    private static AppException CreateUnavailableAccountException()
    {
        return new AppException("UNAUTHORIZED", 401, "Tài khoản không tồn tại hoặc đã bị khóa.");
    }
}
