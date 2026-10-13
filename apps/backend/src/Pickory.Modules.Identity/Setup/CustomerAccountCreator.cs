using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Persistence;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Users;

namespace Pickory.Modules.Identity.Setup;

// Tạo tài khoản khách hàng cố định cho kiểm thử E2E.
public static class CustomerAccountCreator
{
    public static Task CreateAsync(
        IdentityDbContext database,
        string email,
        string password,
        string name,
        CancellationToken cancellationToken)
    {
        // Chuyển tiếp với số điện thoại mặc định cho các lệnh gọi không truyền số điện thoại.
        return CreateAsync(database, email, password, name, "0987654321", cancellationToken);
    }

    public static async Task CreateAsync(
        IdentityDbContext database,
        string email,
        string password,
        string name,
        string? phone,
        CancellationToken cancellationToken)
    {
        // Kiểm tra độ dài mật khẩu trước khi lưu. Mật khẩu ngắn không bảo đảm an toàn.
        if (password.Length < 8)
        {
            throw new InvalidOperationException("Mật khẩu khách hàng phải có tối thiểu 8 ký tự.");
        }

        // Chuẩn hóa email trước khi tìm kiếm. Email hoa và thường cùng thuộc một tài khoản.
        var normalizedEmail = UserInputNormalizer.NormalizeEmail(email);

        // Khởi tạo các vai trò mặc định nếu cơ sở dữ liệu chưa có vai trò nào.
        await IdentityRoleSeeder.SeedAsync(database, cancellationToken);

        // Không tạo lại nếu tài khoản đã tồn tại. Thao tác seed giữ nguyên dữ liệu cũ.
        var existingUser = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
        if (existingUser is not null)
        {
            return;
        }

        // Gán vai trò khách hàng mặc định cho tài khoản mới.
        var customerRole = await database.Roles
            .SingleAsync(role => role.Code == DefaultRoles.CustomerCode, cancellationToken);

        // Đánh dấu email đã xác thực và lưu số điện thoại để biểu mẫu đặt hàng tự điền đủ thông tin.
        var customer = new User
        {
            Email = normalizedEmail,
            EmailVerified = true,
            PasswordHash = PasswordHasher.Hash(password),
            Name = name,
            Phone = phone,
            Kind = UserKind.Customer,
            UpdatedAt = DateTime.UtcNow,
        };
        customer.Roles.Add(new UserRole { User = customer, RoleId = customerRole.Id });
        database.Users.Add(customer);
        await database.SaveChangesAsync(cancellationToken);
    }
}
