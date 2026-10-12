using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.EmailTokens;
using Pickory.Shared.Email;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Configuration;

namespace Pickory.Modules.Identity.Features.ForgotPassword;

// Gửi link đặt lại mật khẩu khi email tồn tại. Mọi nhánh kết thúc giống nhau để không lộ email nào đã đăng ký.
public sealed class ForgotPasswordHandler(
    IdentityDbContext database,
    IEmailSender emailSender,
    IdentityRateLimiters rateLimiters,
    AppConfig config,
    TimeProvider timeProvider)
{
    private const string TimingDefensePassword = "dummy-password-timing-defense";

    public async Task RequestResetAsync(ForgotPasswordRequest request, string clientIp, CancellationToken cancellationToken)
    {
        // Chuẩn hóa email trước khi tra cứu. Email viết hoa và viết thường thuộc cùng một tài khoản.
        var email = UserInputNormalizer.NormalizeEmail(request.Email!);
        // Cắt khoảng trắng của IP. Cùng một máy luôn dùng chung một bộ đếm.
        var ip = clientIp.Trim();

        // Giới hạn theo email để không spam hòm thư một người. Giới hạn theo IP để một máy không quét hàng loạt email.
        var isEmailLimited = rateLimiters.ForgotPasswordEmail.IsRateLimited(email);
        var isIpLimited = rateLimiters.ForgotPasswordIp.IsRateLimited(ip);
        // Request bị chặn vẫn được đếm. Kẻ gọi liên tục không được mở lại sớm hơn.
        rateLimiters.ForgotPasswordEmail.RecordAttempt(email);
        rateLimiters.ForgotPasswordIp.RecordAttempt(ip);

        if (isEmailLimited || isIpLimited)
        {
            // Không trả 429, vì lỗi đó cho kẻ dò biết email có thể tồn tại.
            // Băm một lần để thời gian phản hồi gần bằng nhánh có gửi thư.
            PasswordHasher.Hash(TimingDefensePassword);
            return;
        }

        var user = await database.Users.SingleOrDefaultAsync(value => value.Email == email, cancellationToken);
        // Tài khoản bị khóa hoặc bị xóa không được đặt lại mật khẩu để quay lại.
        if (user is null || user.LockedAt is not null || user.DeletedAt is not null)
        {
            // Email không dùng được cũng băm một lần. Thời gian phản hồi giống nhánh có tài khoản.
            PasswordHasher.Hash(TimingDefensePassword);
            return;
        }

        var rawToken = EmailTokenIssuer.Add(
            database, user.Id, EmailTokenPurpose.Reset, EmailTokenIssuer.ResetLifetime, timeProvider.GetUtcNow().UtcDateTime);
        await database.SaveChangesAsync(cancellationToken);

        // Link trong thư phải là địa chỉ đầy đủ. Đường dẫn tương đối không mở được từ hòm thư.
        await emailSender.SendAsync(
            new EmailMessage(
                user.Email,
                "Đặt lại mật khẩu Pickory",
                $"Chào bạn, vui lòng truy cập liên kết sau để đặt lại mật khẩu: {config.WebOrigin}/reset-password?token={rawToken}",
                new Dictionary<string, string> { ["token"] = rawToken, ["purpose"] = "reset" }),
            cancellationToken);
    }
}
