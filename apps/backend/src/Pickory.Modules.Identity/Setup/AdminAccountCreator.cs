using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Users;

namespace Pickory.Modules.Identity.Setup;

// Tạo admin đầu tiên cho DB mới. Hành vi giữ nguyên `scripts/admin-create.ts` của bản Node.
public static class AdminAccountCreator
{
    public static async Task<AdminCreationResult> CreateAsync(
        IdentityDbContext database,
        string email,
        string password,
        string name,
        CancellationToken cancellationToken)
    {
        // Kiểm tra mật khẩu trước khi chạm DB. Admin không được có mật khẩu yếu.
        if (password.Length < 8)
        {
            throw new InvalidOperationException(
                "Mật khẩu admin là bắt buộc và phải có tối thiểu 8 ký tự (qua biến môi trường ADMIN_PASSWORD hoặc --password).");
        }

        // Chuẩn hóa email trước khi tra cứu. Email viết hoa và viết thường thuộc cùng một tài khoản.
        var normalizedEmail = UserInputNormalizer.NormalizeEmail(email);

        // Role `admin` phải tồn tại trước khi gán. DB mới chưa có role nào.
        await IdentityRoleSeeder.SeedAsync(database, cancellationToken);

        var existingUser = await database.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
        if (existingUser is not null)
        {
            // Tài khoản khách và tài khoản nhân viên tách riêng. Email của khách không thành admin.
            if (existingUser.Kind == UserKind.Customer)
            {
                throw new InvalidOperationException($"Email \"{normalizedEmail}\" đã thuộc tài khoản khách hàng, không thể tạo admin.");
            }

            // Lệnh chạy lại không đổi mật khẩu của admin đã có.
            return AdminCreationResult.AlreadyExists;
        }

        var adminRole = await database.Roles.SingleAsync(role => role.Code == DefaultRoles.AdminCode, cancellationToken);
        // Người chạy lệnh đã biết email này, nên email được coi là đã xác thực.
        var admin = new User
        {
            Email = normalizedEmail,
            EmailVerified = true,
            PasswordHash = PasswordHasher.Hash(password),
            Name = name,
            Kind = UserKind.Staff,
            UpdatedAt = DateTime.UtcNow,
        };
        admin.Roles.Add(new UserRole { User = admin, RoleId = adminRole.Id });
        database.Users.Add(admin);
        await database.SaveChangesAsync(cancellationToken);
        return AdminCreationResult.Created;
    }
}
