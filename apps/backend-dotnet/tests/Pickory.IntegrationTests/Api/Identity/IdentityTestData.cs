using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Roles;

namespace Pickory.IntegrationTests.Api.Identity;

// Tạo dữ liệu identity cho test. Mỗi test dùng email riêng vì DB test không reset giữa các test.
internal static class IdentityTestData
{
    // Mật khẩu mặc định đạt quy tắc độ mạnh. Test đăng nhập dùng lại giá trị này.
    public const string DefaultPassword = "ValidPassword123!";

    // Thêm GUID (Globally Unique Identifier) vào email. Hai test không va chạm ràng buộc email duy nhất trong DB.
    public static string NewEmail(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}@pickory.local";
    }

    // Tạo role bằng seeder thật. Test dùng cùng mã role và quyền như môi trường chạy.
    public static async Task SeedRolesAsync(IntegrationDatabaseFixture database)
    {
        await using var identity = database.CreateIdentityContext();
        await IdentityRoleSeeder.SeedAsync(identity, CancellationToken.None);
    }

    public static async Task<User> CreateUserAsync(
        IntegrationDatabaseFixture database,
        string email,
        UserKind kind,
        string roleCode,
        string? password = DefaultPassword,
        bool emailVerified = false,
        bool locked = false,
        bool deleted = false)
    {
        // Gán role cần bản ghi role có sẵn. Seeder chạy lại nhiều lần vẫn an toàn.
        await SeedRolesAsync(database);
        await using var identity = database.CreateIdentityContext();
        var role = await identity.Roles.SingleAsync(value => value.Code == roleCode);
        // Đặt mốc khóa hoặc xóa trong quá khứ. API coi tài khoản đã khóa hoặc đã xóa ngay khi test chạy.
        var user = new User
        {
            Email = email,
            Name = "Người dùng thử",
            Phone = "0901234567",
            Kind = kind,
            EmailVerified = emailVerified,
            // Băm bằng `PasswordHasher` thật. Đăng nhập trong test so khớp đúng định dạng hash của API.
            PasswordHash = password is null ? null : PasswordHasher.Hash(password),
            LockedAt = locked ? DateTime.UtcNow.AddHours(-1) : null,
            DeletedAt = deleted ? DateTime.UtcNow.AddHours(-1) : null,
            UpdatedAt = DateTime.UtcNow,
        };
        user.Roles.Add(new UserRole { User = user, RoleId = role.Id });
        identity.Users.Add(user);
        await identity.SaveChangesAsync();
        return user;
    }
}
