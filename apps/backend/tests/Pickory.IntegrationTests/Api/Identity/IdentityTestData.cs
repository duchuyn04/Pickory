using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Tokens;

namespace Pickory.IntegrationTests.Api.Identity;

// Tạo dữ liệu identity cho test. Mỗi test dùng email riêng vì DB test không reset giữa các test.
internal static class IdentityTestData
{
    // Mật khẩu mặc định đạt quy tắc độ mạnh. Test đăng nhập dùng lại giá trị này.
    public const string DefaultPassword = "ValidPassword123!";

    // Lưu tạm chuỗi hash theo từng mật khẩu bằng `Lazy<string>` để chỉ băm một lần khi tranh chấp đồng thời.
    private static readonly ConcurrentDictionary<string, Lazy<string>> PasswordHashCache = new();

    // Lấy chuỗi hash từ bộ nhớ tạm hoặc băm mới qua hàm băm khi chưa có trong cache.
    internal static string GetOrCreatePasswordHash(string password, Func<string, string>? hashFactory = null)
    {
        var factory = hashFactory ?? PasswordHasher.Hash;
        // Dùng LazyThreadSafetyMode.ExecutionAndPublication để đảm bảo chỉ có một luồng thực thi hàm băm.
        var lazy = PasswordHashCache.GetOrAdd(
            password,
            key => new Lazy<string>(() => factory(key), LazyThreadSafetyMode.ExecutionAndPublication));
        // Chỉ đọc thuộc tính `Value` của đối tượng `Lazy<string>` mà `GetOrAdd` trả về.
        return lazy.Value;
    }

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
            // Dùng lại chuỗi hash đã băm để giảm thời gian khởi tạo tài khoản kiểm thử.
            PasswordHash = password is null ? null : GetOrCreatePasswordHash(password),
            LockedAt = locked ? DateTime.UtcNow.AddHours(-1) : null,
            DeletedAt = deleted ? DateTime.UtcNow.AddHours(-1) : null,
            UpdatedAt = DateTime.UtcNow,
        };
        user.Roles.Add(new UserRole { User = user, RoleId = role.Id });
        identity.Users.Add(user);
        await identity.SaveChangesAsync();
        return user;
    }

    // Tạo role riêng cho một test. Mã có GUID nên không trùng ràng buộc mã role duy nhất.
    public static async Task<Role> CreateRoleAsync(IntegrationDatabaseFixture database, IReadOnlyList<string> permissions, string? code = null)
    {
        await using var identity = database.CreateIdentityContext();
        var role = new Role { Code = code ?? $"test-role-{Guid.NewGuid():N}", Name = "Role thử" };
        foreach (var permission in permissions)
        {
            role.Permissions.Add(new RolePermission { Permission = permission });
        }

        identity.Roles.Add(role);
        await identity.SaveChangesAsync();
        return role;
    }

    // Tạo phiên trực tiếp trong DB. Cho phép truyền thời hạn phiên để tránh bị middleware tự động gia hạn.
    public static async Task<string> CreateSessionAsync(
        IntegrationDatabaseFixture database,
        string userId,
        TimeSpan? lifetime = null)
    {
        var rawToken = SecureTokens.GenerateToken();
        // Giữ thời hạn mặc định 12 giờ cho phiên nhân viên và các bài test thông thường.
        var sessionLifetime = lifetime ?? TimeSpan.FromHours(12);
        await using var identity = database.CreateIdentityContext();
        identity.Sessions.Add(new Session
        {
            TokenHash = SecureTokens.HashToken(rawToken),
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.Add(sessionLifetime),
        });
        await identity.SaveChangesAsync();
        return rawToken;
    }

    // Tạo nhân viên giữ role cho trước và trả token phiên của nhân viên đó.
    public static async Task<(User User, string SessionToken)> CreateStaffSessionAsync(IntegrationDatabaseFixture database, string roleCode, string prefix = "staff")
    {
        var user = await CreateUserAsync(database, NewEmail(prefix), UserKind.Staff, roleCode, emailVerified: true);
        return (user, await CreateSessionAsync(database, user.Id));
    }
}
