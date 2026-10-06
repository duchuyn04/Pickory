using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// Kiểm tra toàn bộ ma trận phân quyền trên các route back-office của hệ thống.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class PermissionMatrixTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;
    private string _customerCookie = null!;
    private string _salesCookie = null!;
    private string _supportCookie = null!;
    private string _adminCookie = null!;

    public PermissionMatrixTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        // Tắt tự động nạp cookie để test kiểm soát chính xác từng phiên người dùng.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        // Nạp role và quyền mặc định vào cơ sở dữ liệu trước khi tạo phiên kiểm thử.
        await IdentityTestData.SeedRolesAsync(_database);

        _customerCookie = $"pk_session={await CreateSessionAsync(UserKind.Customer, DefaultRoles.CustomerCode, "matrix-cust")}";
        _salesCookie = $"pk_session={await CreateSessionAsync(UserKind.Staff, DefaultRoles.SalesCode, "matrix-sales")}";
        _supportCookie = $"pk_session={await CreateSessionAsync(UserKind.Staff, DefaultRoles.SupportCode, "matrix-supp")}";
        _adminCookie = $"pk_session={await CreateSessionAsync(UserKind.Staff, DefaultRoles.AdminCode, "matrix-admin")}";
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public static TheoryData<string, string, string[]> BackofficeEndpoints => new()
    {
        { "GET", "/api/admin/audit-logs", [Permissions.AuditLogsRead] },
        { "GET", "/api/admin/permissions", [Permissions.RolesManage] },
        { "GET", "/api/admin/roles", [Permissions.RolesManage] },
        { "POST", "/api/admin/roles", [Permissions.RolesManage] },
        { "PATCH", "/api/admin/roles/dummy-id", [Permissions.RolesManage] },
        { "DELETE", "/api/admin/roles/dummy-id", [Permissions.RolesManage] },
        { "GET", "/api/admin/staff", [Permissions.StaffRead] },
        { "POST", "/api/admin/staff/invitations", [Permissions.StaffManage] },
        { "PUT", "/api/admin/staff/dummy-id/roles", [Permissions.StaffManage] },
        { "POST", "/api/admin/staff/dummy-id/lock", [Permissions.StaffManage] },
        { "POST", "/api/admin/staff/dummy-id/unlock", [Permissions.StaffManage] },
        { "GET", "/api/admin/customers", [Permissions.CustomersRead] },
        { "GET", "/api/admin/customers/dummy-id", [Permissions.CustomersRead] },
        { "POST", "/api/admin/customers/dummy-id/lock", [Permissions.CustomersManage] },
        { "POST", "/api/admin/customers/dummy-id/unlock", [Permissions.CustomersManage] },
        { "GET", "/api/admin/ai/usage", [Permissions.AiUsageRead] },
        { "GET", "/api/admin/ai/settings", [Permissions.AiUsageRead] },
        { "PUT", "/api/admin/ai/settings", [Permissions.AiSettingsManage] },
        { "GET", "/api/admin/categories", [Permissions.ProductsRead] },
        { "POST", "/api/admin/categories", [Permissions.ProductsWrite] },
        { "PATCH", "/api/admin/categories/dummy-id", [Permissions.ProductsWrite] },
        { "DELETE", "/api/admin/categories/dummy-id", [Permissions.ProductsWrite] },
        { "POST", "/api/admin/brands", [Permissions.ProductsWrite] },
        { "PATCH", "/api/admin/brands/dummy-id", [Permissions.ProductsWrite] },
        { "DELETE", "/api/admin/brands/dummy-id", [Permissions.ProductsWrite] },
        { "GET", "/api/admin/products", [Permissions.ProductsRead] },
        { "GET", "/api/admin/products/dummy-id", [Permissions.ProductsRead] },
        { "POST", "/api/admin/products", [Permissions.ProductsWrite] },
        { "PATCH", "/api/admin/products/dummy-id", [Permissions.ProductsWrite] },
    };
    [Theory]
    [MemberData(nameof(BackofficeEndpoints))]
    // Khách vãng lai chưa đăng nhập phải nhận mã lỗi 401 trên mọi route back-office.
    public async Task Guest_Receives401Unauthorized(string method, string path, string[] requiredPermissions)
    {
        _ = requiredPermissions;
        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, body: null, cookieHeader: null);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BackofficeEndpoints))]
    // Tài khoản khách hàng không được phép truy cập bất kỳ route back-office nào.
    public async Task Customer_Receives403Forbidden(string method, string path, string[] requiredPermissions)
    {
        _ = requiredPermissions;
        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, body: null, cookieHeader: _customerCookie);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BackofficeEndpoints))]
    // Nhân viên bán hàng chỉ được gọi route khi quyền cần thiết nằm trong role sales.
    public async Task Sales_HasAccessOnlyWhenAuthorized(string method, string path, string[] requiredPermissions)
    {
        var salesPermissions = DefaultRoles.All.Single(role => role.Code == DefaultRoles.SalesCode)
            .Permissions.ToHashSet(StringComparer.Ordinal);
        var hasAccess = requiredPermissions.All(permission => salesPermissions.Contains(permission));

        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, body: null, cookieHeader: _salesCookie);
        var response = await _client.SendAsync(request);

        if (hasAccess)
        {
            // Được cấp quyền thì không bị chặn bởi bộ lọc xác thực hoặc ủy quyền.
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        else
        {
            // Thiếu quyền thì middleware phải từ chối ngay với mã 403.
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(BackofficeEndpoints))]
    // Nhân viên CSKH chỉ được gọi route khi quyền cần thiết nằm trong role support.
    public async Task Support_HasAccessOnlyWhenAuthorized(string method, string path, string[] requiredPermissions)
    {
        var supportPermissions = DefaultRoles.All.Single(role => role.Code == DefaultRoles.SupportCode)
            .Permissions.ToHashSet(StringComparer.Ordinal);
        var hasAccess = requiredPermissions.All(permission => supportPermissions.Contains(permission));

        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, body: null, cookieHeader: _supportCookie);
        var response = await _client.SendAsync(request);

        if (hasAccess)
        {
            // Được cấp quyền thì không bị chặn bởi bộ lọc xác thực hoặc ủy quyền.
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        else
        {
            // Thiếu quyền thì middleware phải từ chối ngay với mã 403.
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(BackofficeEndpoints))]
    // Tài khoản admin có mọi quyền ngầm định nên không bao giờ bị chặn 401 hoặc 403.
    public async Task Admin_HasImplicitAccessToAllEndpoints(string method, string path, string[] requiredPermissions)
    {
        _ = requiredPermissions;
        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, body: null, cookieHeader: _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<string> CreateSessionAsync(UserKind kind, string roleCode, string prefix)
    {
        var user = await IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail(prefix), kind, roleCode, emailVerified: true);
        return await IdentityTestData.CreateSessionAsync(_database, user.Id);
    }
}
