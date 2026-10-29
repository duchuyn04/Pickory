using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Port từ test ID-11 của bản Node cho danh mục quyền và quản lý role.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminRolesTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public AdminRolesTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Fact]
    // Người quản lý role nhận đủ 28 quyền theo thứ tự danh mục để vẽ bảng tick.
    public async Task Permissions_AdminReceivesWholeCatalog()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync(HttpMethod.Get, "/api/admin/permissions", body: null, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var items = document.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(Permissions.All, items.Select(item => item.GetProperty("id").GetString()));
        // Mỗi mục có nhóm, tên và mô tả cho giao diện.
        Assert.Equal("identity", items[0].GetProperty("group").GetString());
        Assert.Equal("Xem nhân viên", items[0].GetProperty("name").GetString());
        Assert.Equal("Xem danh sách tài khoản nhân viên", items[0].GetProperty("description").GetString());
    }

    [Fact]
    // Nhân viên bán hàng không có `roles:manage`, nên không xem được danh mục quyền.
    public async Task Permissions_StaffWithoutRolesManageIsForbidden()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");

        var response = await SendAsync(HttpMethod.Get, "/api/admin/permissions", body: null, salesSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không có đủ quyền thực hiện thao tác này.");
    }

    [Fact]
    // Người quản lý nhân viên cũng cần xem role để chọn role khi mời hoặc gán.
    public async Task ListRoles_AllowsStaffManageWithoutRolesManage()
    {
        var staffManager = await IdentityTestData.CreateRoleAsync(_database, [Permissions.StaffManage]);
        var (_, session) = await IdentityTestData.CreateStaffSessionAsync(_database, staffManager.Code);

        var response = await SendAsync(HttpMethod.Get, "/api/admin/roles", body: null, session);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var roles = document.RootElement.GetProperty("data").EnumerateArray().ToList();
        // Role tạo sẵn đứng trước role tạo trong test, vì danh sách theo thứ tự tạo.
        var codes = roles.Select(role => role.GetProperty("code").GetString()).ToList();
        Assert.True(codes.IndexOf("customer") < codes.IndexOf(staffManager.Code));
        var sales = roles.Single(role => role.GetProperty("code").GetString() == "sales");
        Assert.False(sales.GetProperty("isSystem").GetBoolean());
        // Quyền được sắp xếp để thứ tự ổn định giữa các lần gọi.
        var salesPermissions = sales.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()!).ToList();
        Assert.Equal(salesPermissions.Order(StringComparer.Ordinal), salesPermissions);
        // `userCount` cho giao diện biết role nào xóa được.
        var managerRole = roles.Single(role => role.GetProperty("code").GetString() == staffManager.Code);
        Assert.Equal(1, managerRole.GetProperty("userCount").GetInt32());
    }

    [Fact]
    // Nhân viên không có `roles:manage` và không có `staff:manage` không xem được danh sách role.
    public async Task ListRoles_StaffWithoutEitherPermissionIsForbidden()
    {
        var (_, salesSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales");

        var response = await SendAsync(HttpMethod.Get, "/api/admin/roles", body: null, salesSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không có quyền xem danh sách vai trò.");
    }

    [Fact]
    // Tạo role chuẩn hóa mã, khử quyền trùng và ghi đúng một dòng nhật ký.
    public async Task CreateRole_NormalizesCodeDeduplicatesPermissionsAndWritesAudit()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var code = $"Kho_{Guid.NewGuid():N}";

        var response = await SendAsync(
            HttpMethod.Post,
            "/api/admin/roles",
            new { code = $"  {code}  ", name = " Quản lý kho ", permissions = new[] { Permissions.ProductsRead, Permissions.ProductsWrite, Permissions.ProductsRead } },
            adminSession);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var role = document.RootElement.GetProperty("data");
        // Mã thành chữ thường để `Kho` và `kho` không thành hai role.
        Assert.Equal(code.ToLowerInvariant(), role.GetProperty("code").GetString());
        Assert.Equal("Quản lý kho", role.GetProperty("name").GetString());
        Assert.False(role.GetProperty("isSystem").GetBoolean());
        Assert.Equal([Permissions.ProductsRead, Permissions.ProductsWrite], role.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()));
        Assert.Equal(0, role.GetProperty("userCount").GetInt32());

        // Nhật ký ghi người tạo và bộ quyền sau khi tạo.
        await using var identity = _database.CreateIdentityContext();
        var roleId = role.GetProperty("id").GetString()!;
        var audit = await identity.AuditLogs.SingleAsync(log => log.TargetId == roleId);
        Assert.Equal(admin.Id, audit.ActorId);
        Assert.Equal("role.create", audit.Action);
        Assert.Equal("role", audit.TargetType);
        Assert.Equal(code.ToLowerInvariant(), audit.After!.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("kho hàng", "INVALID_INPUT", "Mã role chỉ được chứa chữ thường, số, gạch dưới và gạch nối.")]
    [InlineData("   ", "VALIDATION_ERROR", "Dữ liệu không hợp lệ (code: Mã role không được để trống.).")]
    // Mã role phải dùng được trong URL. Mã trống hoặc có ký tự lạ bị từ chối.
    public async Task CreateRole_InvalidCodeIsRejected(string code, string expectedCode, string expectedMessage)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync(HttpMethod.Post, "/api/admin/roles", new { code, name = "Kho" }, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, expectedCode, expectedMessage);
    }

    [Fact]
    // API không tin quyền do client gửi. Quyền không có trong danh mục bị từ chối.
    public async Task CreateRole_UnknownPermissionIsRejected()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync(HttpMethod.Post, "/api/admin/roles", new { code = $"role-{Guid.NewGuid():N}", name = "Lạ", permissions = new[] { "orders:hack" } }, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "INVALID_PERMISSION", "Quyền \"orders:hack\" không thuộc danh mục quyền của hệ thống.");
    }

    [Fact]
    // Mã role đã tồn tại nhận lỗi 400 dễ hiểu thay vì lỗi ràng buộc duy nhất của DB.
    public async Task CreateRole_DuplicateCodeIsRejected()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync(HttpMethod.Post, "/api/admin/roles", new { code = "sales", name = "Trùng" }, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "ROLE_ALREADY_EXISTS", "Mã role \"sales\" đã tồn tại.");
    }

    [Fact]
    // Người không phải admin không cấp được quyền mình không có. Họ không tự nâng quyền qua role mới.
    public async Task CreateRole_NonAdminCannotGrantPermissionTheyLack()
    {
        var roleManager = await IdentityTestData.CreateRoleAsync(_database, [Permissions.RolesManage]);
        var (_, session) = await IdentityTestData.CreateStaffSessionAsync(_database, roleManager.Code);

        var response = await SendAsync(HttpMethod.Post, "/api/admin/roles", new { code = $"role-{Guid.NewGuid():N}", name = "Vượt quyền", permissions = new[] { Permissions.StaffManage } }, session);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không thể cấp quyền \"staff:manage\" vì chính bạn không sở hữu quyền này.");
    }

    [Fact]
    // Role hệ thống giữ nguyên để quy tắc phân quyền cốt lõi không bị phá.
    public async Task SystemRole_CannotBeUpdatedOrDeleted()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var adminRoleId = await ReadRoleIdAsync("admin");

        var patchResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/roles/{adminRoleId}", new { name = "Đổi tên" }, adminSession);
        var deleteResponse = await SendAsync(HttpMethod.Delete, $"/api/admin/roles/{adminRoleId}", body: null, adminSession);

        await RegisterTests.AssertErrorAsync(patchResponse, HttpStatusCode.Forbidden, "FORBIDDEN", "Không được phép sửa đổi role hệ thống.");
        await RegisterTests.AssertErrorAsync(deleteResponse, HttpStatusCode.Forbidden, "FORBIDDEN", "Không được phép xóa role hệ thống.");
    }

    [Fact]
    // Người không phải admin không sửa được role mình đang giữ. Họ không tự thêm quyền cho chính mình.
    public async Task UpdateRole_NonAdminCannotEditOwnRole()
    {
        var roleManager = await IdentityTestData.CreateRoleAsync(_database, [Permissions.RolesManage]);
        var (_, session) = await IdentityTestData.CreateStaffSessionAsync(_database, roleManager.Code);

        var response = await SendAsync(HttpMethod.Patch, $"/api/admin/roles/{roleManager.Id}", new { name = "Tự sửa" }, session);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "CANNOT_MODIFY_SELF_ROLE", "Bạn không thể sửa đổi role mà chính mình đang giữ.");
    }

    [Fact]
    // Chỉ đổi tên thì giữ nguyên quyền. Gửi danh sách quyền thì thay toàn bộ quyền và ghi trạng thái trước, sau.
    public async Task UpdateRole_RenameKeepsPermissionsAndPermissionListReplacesThem()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var role = await IdentityTestData.CreateRoleAsync(_database, [Permissions.OrdersRead]);

        var renameResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/roles/{role.Id}", new { name = "Tên mới" }, adminSession);
        var replaceResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/roles/{role.Id}", new { permissions = new[] { Permissions.ReportsRead } }, adminSession);

        using var renameDocument = await ActorHttp.ReadJsonAsync(renameResponse);
        Assert.Equal("Tên mới", renameDocument.RootElement.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal([Permissions.OrdersRead], renameDocument.RootElement.GetProperty("data").GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()));
        using var replaceDocument = await ActorHttp.ReadJsonAsync(replaceResponse);
        Assert.Equal([Permissions.ReportsRead], replaceDocument.RootElement.GetProperty("data").GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()));

        // Nhật ký lần thay quyền ghi bộ quyền trước và sau.
        await using var identity = _database.CreateIdentityContext();
        var audits = await identity.AuditLogs.Where(log => log.TargetId == role.Id && log.Action == "role.update").OrderBy(log => log.CreatedAt).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Equal(Permissions.OrdersRead, audits[1].Before!.RootElement.GetProperty("permissions")[0].GetString());
        Assert.Equal(Permissions.ReportsRead, audits[1].After!.RootElement.GetProperty("permissions")[0].GetString());
    }

    [Fact]
    // Danh sách quyền mới có thể giữ lại quyền cũ. Thao tác thay quyền không được lỗi vì trùng khóa quyền.
    public async Task UpdateRole_PermissionListOverlappingCurrentPermissionsIsSaved()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var role = await IdentityTestData.CreateRoleAsync(_database, [Permissions.OrdersRead, Permissions.OrdersCancel]);

        var response = await SendAsync(HttpMethod.Patch, $"/api/admin/roles/{role.Id}", new { permissions = new[] { Permissions.OrdersRead, Permissions.ReportsRead } }, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // DB chỉ còn bộ quyền mới. Quyền bị bỏ khỏi danh sách không còn hiệu lực.
        await using var identity = _database.CreateIdentityContext();
        var stored = await identity.RolePermissions.Where(value => value.RoleId == role.Id).Select(value => value.Permission).OrderBy(value => value).ToListAsync();
        Assert.Equal([Permissions.OrdersRead, Permissions.ReportsRead], stored);
    }

    [Fact]
    // Role đang có người giữ không xóa được. Người giữ không bị mất quyền mà không ai biết lý do.
    public async Task DeleteRole_InUseIsRejectedAndUnusedIsDeletedWithAudit()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var usedRole = await IdentityTestData.CreateRoleAsync(_database, [Permissions.OrdersRead]);
        await IdentityTestData.CreateStaffSessionAsync(_database, usedRole.Code);
        var unusedRole = await IdentityTestData.CreateRoleAsync(_database, [Permissions.OrdersRead]);

        var inUseResponse = await SendAsync(HttpMethod.Delete, $"/api/admin/roles/{usedRole.Id}", body: null, adminSession);
        var unusedResponse = await SendAsync(HttpMethod.Delete, $"/api/admin/roles/{unusedRole.Id}", body: null, adminSession);

        await RegisterTests.AssertErrorAsync(
            inUseResponse, HttpStatusCode.BadRequest, "ROLE_IN_USE", "Không thể xóa role đang có 1 người dùng giữ. Vui lòng gỡ role trước.");
        Assert.Equal(HttpStatusCode.OK, unusedResponse.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(unusedResponse);
        Assert.True(document.RootElement.GetProperty("data").GetProperty("success").GetBoolean());

        // Role bị xóa cùng quyền của role. Nhật ký giữ lại mã role đã xóa.
        await using var identity = _database.CreateIdentityContext();
        Assert.False(await identity.Roles.AnyAsync(value => value.Id == unusedRole.Id));
        var audit = await identity.AuditLogs.SingleAsync(log => log.TargetId == unusedRole.Id);
        Assert.Equal(admin.Id, audit.ActorId);
        Assert.Equal("role.delete", audit.Action);
        Assert.Equal(unusedRole.Code, audit.Before!.RootElement.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, audit.After!.RootElement.ValueKind);
    }

    [Fact]
    // Id role không tồn tại nhận 404 cho cả sửa và xóa.
    public async Task UnknownRoleId_Returns404()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync(HttpMethod.Delete, $"/api/admin/roles/{Guid.NewGuid()}", body: null, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.NotFound, "ROLE_NOT_FOUND", "Không tìm thấy role.");
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string sessionToken)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(method, path, body, $"pk_session={sessionToken}"));
    }

    private async Task<string> ReadRoleIdAsync(string code)
    {
        await using var identity = _database.CreateIdentityContext();
        return await identity.Roles.Where(role => role.Code == code).Select(role => role.Id).SingleAsync();
    }
}
