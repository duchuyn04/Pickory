using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Passwords;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Setup;
using Xunit;

namespace Pickory.IntegrationTests.Database;

// Port từ test seed của bản Node và từ hành vi của `scripts/admin-create.ts`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class IdentitySetupTests(IntegrationDatabaseFixture database)
{
    [Fact]
    // Seed tạo đủ bốn role với quyền mặc định. Chạy lại không tạo role trùng hoặc quyền trùng.
    public async Task RoleSeeder_CreatesDefaultRolesAndIsIdempotent()
    {
        await using (var first = database.CreateIdentityContext())
        {
            await IdentityRoleSeeder.SeedAsync(first, CancellationToken.None);
        }

        await using (var second = database.CreateIdentityContext())
        {
            await IdentityRoleSeeder.SeedAsync(second, CancellationToken.None);
        }

        await using var identity = database.CreateIdentityContext();
        var roles = await identity.Roles.Include(role => role.Permissions)
            .Where(role => DefaultRoles.All.Select(defaultRole => defaultRole.Code).Contains(role.Code))
            .ToDictionaryAsync(role => role.Code);
        Assert.Equal(4, roles.Count);
        // Quyền của `sales` và `support` khớp bảng mặc định và không bị nhân đôi.
        foreach (var defaultRole in DefaultRoles.All)
        {
            Assert.Equal(
                defaultRole.Permissions.Order(StringComparer.Ordinal),
                roles[defaultRole.Code].Permissions.Select(permission => permission.Permission).Order(StringComparer.Ordinal));
            Assert.Equal(defaultRole.IsSystem, roles[defaultRole.Code].IsSystem);
        }
    }

    [Fact]
    // Quyền đã bị xóa khỏi code nhưng còn trong DB được ghi cảnh báo. Người vận hành biết dữ liệu cũ cần dọn.
    public async Task StoredPermissionCheck_WarnsAboutUnknownPermissions()
    {
        var legacyRole = await IdentityTestData.CreateRoleAsync(database, ["legacy:removed"]);
        var logger = new RecordingLogger();

        await using (var identity = database.CreateIdentityContext())
        {
            await StoredPermissionCheck.WarnAboutUnknownPermissionsAsync(identity, logger, CancellationToken.None);
        }

        var warning = Assert.Single(logger.Warnings, message => message.Contains(legacyRole.Code, StringComparison.Ordinal));
        Assert.Contains("legacy:removed", warning);
    }

    [Fact]
    // Lệnh tạo admin tạo nhân viên giữ role `admin`, đã xác thực email và đăng nhập được bằng mật khẩu.
    public async Task AdminAccountCreator_CreatesVerifiedAdmin()
    {
        var email = IdentityTestData.NewEmail("first-admin");

        AdminCreationResult result;
        await using (var identity = database.CreateIdentityContext())
        {
            result = await AdminAccountCreator.CreateAsync(identity, email.ToUpperInvariant(), "Admin@123456", "Quản trị viên", CancellationToken.None);
        }

        Assert.Equal(AdminCreationResult.Created, result);
        await using var verify = database.CreateIdentityContext();
        var admin = await verify.Users.Include(user => user.Roles).ThenInclude(userRole => userRole.Role).SingleAsync(user => user.Email == email);
        Assert.Equal(UserKind.Staff, admin.Kind);
        Assert.True(admin.EmailVerified);
        Assert.Equal(["admin"], admin.Roles.Select(userRole => userRole.Role!.Code));
        Assert.True(PasswordHasher.Verify(admin.PasswordHash!, "Admin@123456"));
    }

    [Fact]
    // Chạy lại lệnh với email nhân viên đã có thì không tạo thêm và không đổi mật khẩu.
    public async Task AdminAccountCreator_ExistingStaffIsLeftUnchanged()
    {
        var staff = await IdentityTestData.CreateUserAsync(database, IdentityTestData.NewEmail("existing-admin"), UserKind.Staff, "admin");

        await using var identity = database.CreateIdentityContext();
        var result = await AdminAccountCreator.CreateAsync(identity, staff.Email, "OtherPassword123!", "Tên khác", CancellationToken.None);

        Assert.Equal(AdminCreationResult.AlreadyExists, result);
        var stored = await identity.Users.AsNoTracking().SingleAsync(user => user.Id == staff.Id);
        Assert.True(PasswordHasher.Verify(stored.PasswordHash!, IdentityTestData.DefaultPassword));
    }

    [Theory]
    [InlineData(UserKind.Customer, "Admin@123456", "đã thuộc tài khoản khách hàng")]
    [InlineData(null, "short", "tối thiểu 8 ký tự")]
    // Email của khách không thành admin. Mật khẩu ngắn hơn 8 ký tự bị từ chối trước khi chạm DB.
    public async Task AdminAccountCreator_RejectsCustomerEmailAndShortPassword(UserKind? existingKind, string password, string expectedMessagePart)
    {
        var email = IdentityTestData.NewEmail("admin-rejected");
        if (existingKind is { } kind)
        {
            await IdentityTestData.CreateUserAsync(database, email, kind, "customer");
        }

        await using var identity = database.CreateIdentityContext();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminAccountCreator.CreateAsync(identity, email, password, "Quản trị viên", CancellationToken.None));

        Assert.Contains(expectedMessagePart, exception.Message);
    }

    // Logger giả ghi lại cảnh báo. Test đọc nội dung cảnh báo mà không cấu hình nhà cung cấp log.
    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
