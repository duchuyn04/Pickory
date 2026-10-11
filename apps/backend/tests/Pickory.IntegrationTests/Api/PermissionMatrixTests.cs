using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Shipping;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// Kiểm tra toàn bộ ma trận phân quyền trên các route back-office của hệ thống.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class PermissionMatrixTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;
    private readonly PickoryWebApplicationFactory _factory;
    private string _customerCookie = null!;
    private string _salesCookie = null!;
    private string _supportCookie = null!;
    private string _adminCookie = null!;

    public PermissionMatrixTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _factory = factory;
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
        { "PATCH", "/api/admin/staff/dummy-id", [Permissions.StaffManage] },
        { "GET", "/api/admin/customers", [Permissions.CustomersRead] },
        { "GET", "/api/admin/customers/dummy-id", [Permissions.CustomersRead] },
        { "PATCH", "/api/admin/customers/dummy-id", [Permissions.CustomersManage] },
        { "GET", "/api/admin/ai/usage", [Permissions.AiUsageRead] },
        { "GET", "/api/admin/ai/settings", [Permissions.AiUsageRead] },
        { "PUT", "/api/admin/ai/settings", [Permissions.AiSettingsManage] },
        { "GET", "/api/admin/shipping/settings", [Permissions.ShippingSettingsManage] },
        { "PUT", "/api/admin/shipping/settings", [Permissions.ShippingSettingsManage] },
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
        { "PUT", "/api/admin/products/dummy-id/status", [Permissions.ProductsWrite] },
        { "DELETE", "/api/admin/products/dummy-id", [Permissions.ProductsWrite] },
        { "POST", "/api/admin/uploads/signature", [Permissions.ProductsWrite] },
        { "POST", "/api/admin/skus/dummy-id/movements", [Permissions.InventoryWrite] },
        { "GET", "/api/admin/skus/dummy-id/movements", [Permissions.ProductsRead] },
        { "GET", "/api/admin/inventory/low-stock", [Permissions.ProductsRead] },
    };
    [Theory]
    [MemberData(nameof(BackofficeEndpoints))]
    // Khách vãng lai chưa đăng nhập phải nhận mã lỗi 401 trên mọi route back-office.
    public async Task Guest_Receives401Unauthorized(string method, string path, string[] requiredPermissions)
    {
        _ = requiredPermissions;
        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, BodyFor(method, path), cookieHeader: null);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BackofficeEndpoints))]
    // Tài khoản khách hàng không được phép truy cập bất kỳ route back-office nào.
    public async Task Customer_Receives403Forbidden(string method, string path, string[] requiredPermissions)
    {
        _ = requiredPermissions;
        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, BodyFor(method, path), cookieHeader: _customerCookie);
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

        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, BodyFor(method, path), cookieHeader: _salesCookie);
        var response = await _client.SendAsync(request);

        if (hasAccess)
        {
            // Được cấp quyền thì không bị chặn bởi bộ lọc xác thực hoặc ủy quyền.
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            // Dòng có body hợp lệ không được dừng ở bước kiểm body. Các dòng khác gửi body rỗng nên có thể trả 400.
            if (BodyFor(method, path) is not null)
            {
                Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
            }
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

        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, BodyFor(method, path), cookieHeader: _supportCookie);
        var response = await _client.SendAsync(request);

        if (hasAccess)
        {
            // Được cấp quyền thì không bị chặn bởi bộ lọc xác thực hoặc ủy quyền.
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            // Dòng có body hợp lệ không được dừng ở bước kiểm body. Các dòng khác gửi body rỗng nên có thể trả 400.
            if (BodyFor(method, path) is not null)
            {
                Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
            }
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
        var request = ActorHttp.CreateJsonRequest(new HttpMethod(method), path, BodyFor(method, path), cookieHeader: _adminCookie);
        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        // Dòng có body hợp lệ không được dừng ở bước kiểm body. Các dòng khác gửi body rỗng nên có thể trả 400.
        if (BodyFor(method, path) is not null)
        {
            Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/api/admin/staff/dummy-id")]
    [InlineData("/api/admin/customers/dummy-id")]
    // Kiểm quyền chạy trước kiểm body. Role thiếu quyền gửi body sai vẫn nhận 403, không nhận 400.
    public async Task LockRoutes_ForbidWithoutPermissionBeforeBodyValidation(string path)
    {
        // Role support không có `staff:manage` và `customers:manage`.
        var request = ActorHttp.CreateJsonRequest(HttpMethod.Patch, path, new { }, cookieHeader: _supportCookie);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Route mới của catalog phải có trong ma trận để không bỏ sót phép kiểm quyền.
    public void BackofficeEndpoints_CoversAllCatalogAdminEndpoints()
    {
        AssertMatrixMatchesModuleAdminEndpoints(
            "catalog", typeof(CatalogModule).Assembly, "Pickory.Modules.Catalog", CatalogPermissions.All);
    }

    [Fact]
    // SHIP-09: route back-office của shipping phải có trong ma trận, kể cả route thêm sau này.
    public void BackofficeEndpoints_CoversAllShippingAdminEndpoints()
    {
        // Shipping chỉ dùng một quyền back-office. Quyền này nằm trong danh sách chung của identity.
        AssertMatrixMatchesModuleAdminEndpoints(
            "shipping", typeof(ShippingModule).Assembly, "Pickory.Modules.Shipping", [Permissions.ShippingSettingsManage]);
    }

    // Đối chiếu hai chiều giữa route `/api/admin/` của một module và các dòng ma trận dùng quyền của module đó.
    private void AssertMatrixMatchesModuleAdminEndpoints(
        string moduleName, Assembly moduleAssembly, string moduleNamespace, IEnumerable<string> modulePermissions)
    {
        // Dùng endpoint của ứng dụng thật để route mới không cần thêm vào danh sách kiểm thử riêng.
        var endpointDataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var moduleAdminEndpoints = new List<(string Method, string Pattern)>();

        foreach (var endpoint in endpointDataSource.Endpoints)
        {
            // Chỉ `RouteEndpoint` có `RoutePattern` để đối chiếu với ma trận.
            if (endpoint is not RouteEndpoint routeEndpoint)
            {
                continue;
            }

            var rawPattern = routeEndpoint.RoutePattern.RawText;
            // Route không có đường dẫn không thể ghép với dòng ma trận theo phương thức và đường dẫn.
            if (string.IsNullOrWhiteSpace(rawPattern))
            {
                continue;
            }

            // Ma trận luôn ghi đường dẫn với dấu `/` đầu tiên; `RoutePattern` có thể không ghi dấu đó.
            var normalizedPattern = rawPattern.StartsWith('/') ? rawPattern : "/" + rawPattern;

            // Kiểu khai báo của handler giúp giới hạn phép kiểm vào module đang xét.
            var methodInfo = endpoint.Metadata.GetMetadata<MethodInfo>()
                ?? endpoint.Metadata.OfType<Delegate>().FirstOrDefault()?.Method
                ?? endpoint.RequestDelegate?.Method;

            var declaringType = methodInfo?.DeclaringType;
            var isModuleEndpoint = declaringType != null &&
                (declaringType.Assembly == moduleAssembly ||
                 declaringType.Namespace?.StartsWith(moduleNamespace, StringComparison.Ordinal) == true);

            // Route của module khác do phép kiểm của module đó đối chiếu.
            if (!isModuleEndpoint)
            {
                continue;
            }

            // Route công khai không yêu cầu quyền quản trị và không có trong ma trận.
            if (!normalizedPattern.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Route thiếu phương thức HTTP không có cặp phương thức và đường dẫn để đối chiếu.
            var httpMethodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
            var httpMethods = httpMethodMetadata?.HttpMethods;
            if (httpMethods is null || httpMethods.Count == 0)
            {
                continue;
            }

            // Một route có thể chấp nhận nhiều phương thức HTTP, mỗi phương thức cần một dòng ma trận.
            foreach (var method in httpMethods)
            {
                moduleAdminEndpoints.Add((method.ToUpperInvariant(), normalizedPattern));
            }
        }

        // Nếu bộ nhận diện route hỏng, danh sách rỗng sẽ làm phép so sánh bao phủ luôn xanh.
        Assert.NotEmpty(moduleAdminEndpoints);

        // Ma trận dùng đường dẫn cụ thể để gửi request nên cần khóa tra cứu cùng dạng.
        var matrixRows = BackofficeEndpoints
            .Select(row => new
            {
                Method = ((string)row[0]).ToUpperInvariant(),
                Path = (string)row[1],
                RequiredPermissions = (string[])row[2]
            })
            .ToList();

        var matrixLookup = matrixRows
            .Select(row => (row.Method, row.Path))
            .ToHashSet();

        // Route thiếu dòng ma trận sẽ không được kiểm tra quyền cho các tài khoản.
        var missingRoutes = new List<string>();
        foreach (var (method, pattern) in moduleAdminEndpoints)
        {
            // Ma trận dùng `dummy-id` thay cho tham số của đường dẫn.
            var matrixPath = NormalizeRoutePatternForMatrix(pattern);
            if (!matrixLookup.Contains((method, matrixPath)))
            {
                missingRoutes.Add($"{method} {pattern}");
            }
        }

        // Liệt kê từng route thiếu để người sửa biết chính xác dòng cần bổ sung.
        Assert.True(
            missingRoutes.Count == 0,
            $"Thiếu route quản trị {moduleName} trong BackofficeEndpoints: {string.Join(", ", missingRoutes)}");

        // Dòng ma trận không có route ứng dụng có thể che sai lệch khi route bị xóa.
        var modulePermissionSet = modulePermissions.ToHashSet(StringComparer.Ordinal);
        var registeredModuleRoutes = moduleAdminEndpoints
            .Select(endpoint => (endpoint.Method, Path: NormalizeRoutePatternForMatrix(endpoint.Pattern)))
            .ToHashSet();

        var unknownMatrixRoutes = matrixRows
            .Where(row => row.RequiredPermissions.Any(permission => modulePermissionSet.Contains(permission)))
            .Where(row => !registeredModuleRoutes.Contains((row.Method, row.Path)))
            .Select(row => $"{row.Method} {row.Path}")
            .ToList();

        // Liệt kê dòng thừa để phát hiện route của module đã bị xóa khỏi ứng dụng.
        Assert.True(
            unknownMatrixRoutes.Count == 0,
            $"Ma trận BackofficeEndpoints chứa route {moduleName} không tồn tại: {string.Join(", ", unknownMatrixRoutes)}");
    }

    // Route `PATCH` khóa tài khoản đọc body trước khi chạy nghiệp vụ, nên dòng ma trận cần body hợp lệ. Các route còn lại không cần body.
    private static object? BodyFor(string method, string path)
    {
        var isLockRoute = method == "PATCH" && (path == "/api/admin/staff/dummy-id" || path == "/api/admin/customers/dummy-id");
        return isLockRoute ? new { locked = true } : null;
    }

    // Ma trận dùng `dummy-id` trong request thử quyền nên cần cùng định dạng với route ứng dụng.
    private static string NormalizeRoutePatternForMatrix(string pattern)
    {
        var normalizedPattern = pattern.StartsWith('/') ? pattern : "/" + pattern;
        return Regex.Replace(normalizedPattern, @"\{[^}]+\}", "dummy-id");
    }

    private async Task<string> CreateSessionAsync(UserKind kind, string roleCode, string prefix)
    {
        var user = await IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail(prefix), kind, roleCode, emailVerified: true);
        return await IdentityTestData.CreateSessionAsync(_database, user.Id);
    }
}
