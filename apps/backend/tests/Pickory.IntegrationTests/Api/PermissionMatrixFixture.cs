using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// Khởi tạo và lưu trữ bốn tài khoản cùng phiên kiểm thử cho PermissionMatrixTests.
public sealed class PermissionMatrixFixture(ApiDatabaseFixture database) : IAsyncLifetime
{
    public string CustomerCookie { get; private set; } = null!;
    public string SalesCookie { get; private set; } = null!;
    public string SupportCookie { get; private set; } = null!;
    public string AdminCookie { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Nạp vai trò và quyền mặc định vào cơ sở dữ liệu trước khi tạo phiên.
        await IdentityTestData.SeedRolesAsync(database);

        // Khởi tạo phiên cho từng loại vai trò để dùng chung suốt lớp kiểm thử.
        // Phiên khách hàng nhận thời hạn 30 ngày để tránh bị `ActorMiddleware` gia hạn trong quá trình test.
        CustomerCookie = $"pk_session={await CreateSessionAsync(UserKind.Customer, DefaultRoles.CustomerCode, "matrix-cust", TimeSpan.FromDays(30))}";
        SalesCookie = $"pk_session={await CreateSessionAsync(UserKind.Staff, DefaultRoles.SalesCode, "matrix-sales")}";
        SupportCookie = $"pk_session={await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode, "matrix-supp")}";
        AdminCookie = $"pk_session={await CreateSessionAsync(UserKind.Staff, DefaultRoles.AdminCode, "matrix-admin")}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Tạo người dùng và phiên kiểm thử tương ứng trong cơ sở dữ liệu.
    private async Task<string> CreateSessionAsync(UserKind kind, string roleCode, string prefix, TimeSpan? lifetime = null)
    {
        var user = await IdentityTestData.CreateUserAsync(database, IdentityTestData.NewEmail(prefix), kind, roleCode, emailVerified: true);
        return await IdentityTestData.CreateSessionAsync(database, user.Id, lifetime);
    }
}
