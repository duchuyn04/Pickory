using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.EmailTokens;
using Pickory.Modules.Identity.Emails;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Features.Register;

// Tạo tài khoản khách, gửi thư xác thực và đăng nhập luôn.
public sealed class RegisterHandler(
    IdentityDbContext database,
    IEmailSender emailSender,
    IEventDispatcher eventDispatcher,
    TimeProvider timeProvider)
{
    public async Task<(UserProfileResponse User, IssuedSession Session)> RegisterAsync(
        RegisterRequest request,
        string anonymousId,
        CancellationToken cancellationToken)
    {
        // Chuẩn hóa email trước khi tra cứu. Email viết hoa và viết thường thuộc cùng một tài khoản.
        var email = UserInputNormalizer.NormalizeEmail(request.Email!);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Email của khách và của nhân viên nhận cùng thông báo. Người ngoài không dò được email nào thuộc nhân viên.
        if (await database.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            throw new AppException("EMAIL_ALREADY_EXISTS", 400, "Email này đã được sử dụng. Vui lòng đăng nhập hoặc chọn email khác.");
        }

        // Thiếu role `customer` là lỗi cấu hình do chưa seed. Lỗi này trả 500, không phải lỗi của người dùng.
        var customerRole = await database.Roles.SingleOrDefaultAsync(role => role.Code == DefaultRoles.CustomerCode, cancellationToken)
            ?? throw new InvalidOperationException("Role customer chưa được seed trong DB.");

        var user = new User
        {
            Email = email,
            // Email chỉ được xác thực khi người dùng bấm link trong thư.
            EmailVerified = false,
            PasswordHash = PasswordHasher.Hash(request.Password!),
            Name = UserInputNormalizer.TrimToNull(request.Name),
            // Validator đã kiểm định dạng. Lưu về dạng `0xxxxxxxxx` để các module sau dùng một dạng số.
            Phone = ProfileFieldRules.NormalizePhone(request.Phone),
            Kind = UserKind.Customer,
            UpdatedAt = now,
        };
        user.Roles.Add(new UserRole { User = user, RoleId = customerRole.Id });
        database.Users.Add(user);
        // Tài khoản và token xác thực lưu cùng một lần. Không có tài khoản nào thiếu link xác thực.
        var verifyToken = EmailTokenIssuer.Add(database, user.Id, EmailTokenPurpose.Verify, EmailTokenIssuer.VerifyLifetime, now);
        await database.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            new EmailMessage(
                user.Email,
                "Xác thực tài khoản Pickory",
                $"Chào bạn, vui lòng truy cập link sau để xác thực tài khoản: /verify-email?token={verifyToken}",
                new Dictionary<string, string> { ["token"] = verifyToken, ["purpose"] = "verify" }),
            cancellationToken);

        // Khách không phải nhập lại thông tin vừa gõ. Phiên khách có hạn 30 ngày.
        var session = await SessionIssuer.IssueAsync(database, user.Id, SessionCookies.CustomerSessionLifetime, now, cancellationToken);

        // Phát sự kiện sau khi lưu xong. Module giỏ hàng gộp dữ liệu của mã ẩn danh vào tài khoản mới.
        await eventDispatcher.PublishAsync(new UserLinkedEvent(anonymousId, user.Id), cancellationToken);

        return (new UserProfileResponse(user.Id, user.Email, user.Name, user.Phone, UserProfileResponse.CustomerKind), session);
    }
}
