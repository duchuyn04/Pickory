using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Features.Login;

// Đăng nhập bằng email và mật khẩu cho khách và nhân viên.
public sealed class LoginHandler(
    IdentityDbContext database,
    IdentityRateLimiters rateLimiters,
    IEventDispatcher eventDispatcher,
    TimeProvider timeProvider)
{
    public async Task<(UserProfileResponse User, IssuedSession Session)> LoginCustomerAsync(
        LoginRequest request,
        string clientIp,
        string anonymousId,
        CancellationToken cancellationToken)
    {
        var user = await VerifyCredentialsAsync(request, UserKind.Customer, clientIp, cancellationToken);

        // Phiên khách có hạn 30 ngày và tự gia hạn trong actor middleware. Khách mua sắm không phải đăng nhập liên tục.
        var session = await SessionIssuer.IssueAsync(
            database, user.Id, SessionCookies.CustomerSessionLifetime, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        // Gộp giỏ hàng của mã ẩn danh vào tài khoản. Handler lỗi chỉ bị ghi log, nên đăng nhập không thất bại.
        await eventDispatcher.PublishAsync(new UserLinkedEvent(anonymousId, user.Id), cancellationToken);

        return (new UserProfileResponse(user.Id, user.Email, user.Name, user.Phone, UserProfileResponse.CustomerKind), session);
    }

    public async Task<(UserSummaryResponse User, IssuedSession Session)> LoginStaffAsync(
        LoginRequest request,
        string clientIp,
        CancellationToken cancellationToken)
    {
        var user = await VerifyCredentialsAsync(request, UserKind.Staff, clientIp, cancellationToken);

        // Phiên nhân viên có hạn 12 giờ và không gia hạn. Phiên bị đánh cắp của tài khoản quyền lớn chỉ dùng được ngắn hạn.
        var session = await SessionIssuer.IssueAsync(
            database, user.Id, SessionCookies.StaffSessionLifetime, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        return (new UserSummaryResponse(user.Id, user.Email, user.Name, UserSummaryResponse.StaffKind), session);
    }

    private async Task<User> VerifyCredentialsAsync(LoginRequest request, UserKind expectedKind, string clientIp, CancellationToken cancellationToken)
    {
        // Chuẩn hóa email trước khi tra cứu. Email viết hoa và viết thường thuộc cùng một tài khoản.
        var email = UserInputNormalizer.NormalizeEmail(request.Email!);
        var password = request.Password!;

        // Kiểm tra bộ đếm trước mọi bước khác. Người đang bị khóa không làm máy chủ tốn CPU băm mật khẩu.
        rateLimiters.Login.AssertNotLocked(email, clientIp);

        var user = await database.Users.SingleOrDefaultAsync(value => value.Email == email, cancellationToken);
        // Mọi lý do không đăng nhập được dùng chung một nhánh. Thông báo lỗi không cho biết tài khoản có tồn tại hay không.
        if (user is null || user.Kind != expectedKind || user.LockedAt is not null || user.DeletedAt is not null || user.PasswordHash is null)
        {
            // Băm với hash giả để thời gian phản hồi gần bằng nhánh có tài khoản. Kẻ dò email không đo được khác biệt.
            PasswordHasher.Verify(PasswordHasher.DummyHash, password);
            // Email không tồn tại cũng bị đếm. Kẻ dò email cũng bị giới hạn.
            rateLimiters.Login.RecordFailure(email, clientIp);
            throw CreateInvalidCredentialsException();
        }

        if (!PasswordHasher.Verify(user.PasswordHash, password))
        {
            rateLimiters.Login.RecordFailure(email, clientIp);
            throw CreateInvalidCredentialsException();
        }

        // Đăng nhập đúng thì xóa bộ đếm. Lần gõ nhầm trước đó không cộng dồn vào lần sau.
        rateLimiters.Login.RecordSuccess(email, clientIp);
        return user;
    }

    // Cùng mã và cùng thông báo cho mọi lỗi đăng nhập. Người ngoài không phân biệt được sai email với sai mật khẩu.
    private static AppException CreateInvalidCredentialsException()
    {
        return new AppException("INVALID_CREDENTIALS", 401, "Email hoặc mật khẩu không chính xác.");
    }
}
