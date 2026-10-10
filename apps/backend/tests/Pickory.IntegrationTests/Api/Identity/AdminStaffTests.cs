using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Features.AdminStaff;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Tokens;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.IntegrationTests.Api.Identity;

// Port từ test ID-12, ID-13 và ID-15 của bản Node cho mời, gán role, khóa và mở khóa nhân viên.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminStaffTests : IClassFixture<PickoryWebApplicationFactory>
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public AdminStaffTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Test đăng nhập nhân viên mới mời. Bộ đếm đăng nhập sai bắt đầu trống.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
    }

    [Fact]
    // Danh sách nhân viên không chứa hash mật khẩu và không chứa khách hàng.
    public async Task ListStaff_ReturnsStaffWithRolesWithoutPasswordHash()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var (sales, _) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "list-sales");
        var customer = await IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail("list-customer"), UserKind.Customer, "customer");

        var response = await SendAsync(HttpMethod.Get, "/api/admin/staff", body: null, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var staff = document.RootElement.GetProperty("data").EnumerateArray().ToList();
        var salesRow = staff.Single(row => row.GetProperty("id").GetString() == sales.Id);
        Assert.Equal(["sales"], salesRow.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        Assert.Equal("staff", salesRow.GetProperty("kind").GetString());
        Assert.False(salesRow.TryGetProperty("passwordHash", out _));
        Assert.DoesNotContain(staff, row => row.GetProperty("id").GetString() == customer.Id);
    }

    [Fact]
    // Lời mời tạo nhân viên chưa có mật khẩu, gửi link 72 giờ và ghi nhật ký. Nhân viên đặt mật khẩu rồi đăng nhập được.
    public async Task Invite_CreatesStaffSendsSeventyTwoHourLinkAndAllowsFirstLogin()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var email = IdentityTestData.NewEmail("invited");

        var response = await SendAsync(
            HttpMethod.Post, "/api/admin/staff/invitations", new { email = email.ToUpperInvariant(), name = " Nhân Viên Mới ", roleCodes = new[] { "sales", "support", "sales" } }, adminSession);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        var staffId = data.GetProperty("id").GetString()!;
        Assert.Equal(email, data.GetProperty("email").GetString());
        Assert.Equal("Nhân Viên Mới", data.GetProperty("name").GetString());
        // Role gửi hai lần chỉ được gán một lần.
        Assert.Equal(["sales", "support"], data.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).Order());

        // Tài khoản chưa có mật khẩu. Nhân viên tự đặt mật khẩu qua link mời.
        await using (var identity = _database.CreateIdentityContext())
        {
            var stored = await identity.Users.SingleAsync(value => value.Id == staffId);
            Assert.Null(stored.PasswordHash);
            Assert.Equal(UserKind.Staff, stored.Kind);
            var inviteToken = await identity.EmailTokens.SingleAsync(value => value.UserId == staffId);
            Assert.Equal(EmailTokenPurpose.Invite, inviteToken.Purpose);
            Assert.Equal(DateTime.UtcNow.AddHours(72), inviteToken.ExpiresAt, TimeSpan.FromMinutes(1));
            var audit = await identity.AuditLogs.SingleAsync(log => log.TargetId == staffId);
            Assert.Equal(admin.Id, audit.ActorId);
            Assert.Equal("staff.invite", audit.Action);
        }

        // Link đầy đủ chứa `WEB_ORIGIN`. Đường dẫn tương đối không mở được từ hòm thư.
        var message = Assert.Single(_factory.EmailSender.MessagesTo(email));
        Assert.Equal("Lời mời tham gia quản trị Pickory", message.Subject);
        Assert.Equal("invite", message.Metadata["purpose"]);
        Assert.Contains($"http://localhost:5173/reset-password?token={message.Metadata["token"]}", message.Text);

        var resetResponse = await SendAsync(HttpMethod.Put, "/api/auth/password", new { token = message.Metadata["token"], newPassword = "StaffPassword123!" }, sessionToken: null);
        var loginResponse = await SendAsync(HttpMethod.Post, "/api/admin/auth/sessions", new { email, password = "StaffPassword123!" }, sessionToken: null);
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, loginResponse.StatusCode);
    }

    [Theory]
    [InlineData(UserKind.Customer, "customer", "Email này đã thuộc tài khoản khách hàng, không thể mời làm nhân viên.")]
    [InlineData(UserKind.Staff, "sales", "Email này đã được sử dụng cho một nhân viên khác.")]
    // Email là duy nhất toàn hệ thống. Admin nhận thông báo rõ vì admin không phải người lạ cần che giấu.
    public async Task Invite_ExistingEmailIsRejected(UserKind kind, string roleCode, string expectedMessage)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var existing = await IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail("taken"), kind, roleCode);

        var response = await SendAsync(HttpMethod.Post, "/api/admin/staff/invitations", new { email = existing.Email, name = "Trùng", roleCodes = new[] { "sales" } }, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "EMAIL_ALREADY_EXISTS", expectedMessage);
    }

    [Theory]
    [InlineData("customer", "Không thể gán role customer cho tài khoản nhân viên.")]
    [InlineData("khong-ton-tai", "Một hoặc nhiều mã role không tồn tại trong hệ thống.")]
    // Nhân viên không mang role khách hàng. Mã role không tồn tại cũng bị từ chối.
    public async Task Invite_InvalidRoleIsRejected(string roleCode, string expectedMessage)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync(
            HttpMethod.Post, "/api/admin/staff/invitations", new { email = IdentityTestData.NewEmail("bad-role"), name = "Sai role", roleCodes = new[] { roleCode } }, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "INVALID_ROLE", expectedMessage);
    }

    [Fact]
    // Lời mời cần ít nhất một role. Nhân viên không có role nào thì không làm được việc gì.
    public async Task Invite_EmptyRoleListIsRejected()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync(
            HttpMethod.Post, "/api/admin/staff/invitations", new { email = IdentityTestData.NewEmail("no-role"), name = "Không role", roleCodes = Array.Empty<string>() }, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Dữ liệu không hợp lệ (roleCodes: Cần chọn ít nhất một vai trò.).");
    }

    [Fact]
    // Chỉ admin mới mời được admin. Người quản lý nhân viên không tạo được tài khoản có mọi quyền.
    public async Task Invite_NonAdminCannotInviteAdmin()
    {
        var (_, session) = await CreateStaffManagerSessionAsync([Permissions.StaffManage]);

        var response = await SendAsync(
            HttpMethod.Post, "/api/admin/staff/invitations", new { email = IdentityTestData.NewEmail("new-admin"), name = "Admin mới", roleCodes = new[] { "admin" } }, session);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN", "Chỉ quản trị viên (admin) mới có quyền mời tài khoản làm admin.");
    }

    [Fact]
    // Người quản lý nhân viên không mời được người vào role có quyền mình không có.
    public async Task Invite_NonAdminCannotInviteIntoStrongerRole()
    {
        var (_, session) = await CreateStaffManagerSessionAsync([Permissions.StaffManage]);

        var response = await SendAsync(
            HttpMethod.Post, "/api/admin/staff/invitations", new { email = IdentityTestData.NewEmail("stronger"), name = "Mạnh hơn", roleCodes = new[] { "sales" } }, session);

        await RegisterTests.AssertErrorAsync(
            response, HttpStatusCode.Forbidden, "FORBIDDEN", "Bạn không thể mời nhân viên với role chứa quyền \"ai-usage:read\" vì chính bạn không sở hữu quyền này.");
    }

    [Fact]
    // Gán nhiều role cho nhân viên. Request kế tiếp của nhân viên đó nhận hợp quyền của mọi role.
    public async Task AssignRoles_ReplacesRolesAndPermissionsTakeEffectImmediately()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var (target, targetSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "assign-target");

        var response = await SendAsync(HttpMethod.Put, $"/api/admin/staff/{target.Id}/roles", new { roleCodes = new[] { "sales", "support" } }, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal(target.Id, document.RootElement.GetProperty("data").GetProperty("userId").GetString());

        // Quyền nạp lại ở mỗi request, nên phiên hiện có nhận quyền của cả hai role.
        var meResponse = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_session={targetSession}"));
        using var meDocument = await ActorHttp.ReadJsonAsync(meResponse);
        var permissions = meDocument.RootElement.GetProperty("data").GetProperty("permissions").EnumerateArray().Select(value => value.GetString()).ToList();
        Assert.Contains(Permissions.OrdersUpdateStatus, permissions);
        Assert.Contains(Permissions.ReviewsModerate, permissions);

        // Nhật ký ghi role trước và sau khi gán.
        await using var identity = _database.CreateIdentityContext();
        var audit = await identity.AuditLogs.SingleAsync(log => log.TargetId == target.Id && log.Action == "staff.roles_update");
        Assert.Equal(admin.Id, audit.ActorId);
        Assert.Equal("sales", audit.Before!.RootElement.GetProperty("roles")[0].GetString());
    }

    [Fact]
    // Gỡ hết role xóa mọi phiên của nhân viên. Nhân viên không còn quyền nào để giữ phiên.
    public async Task AssignRoles_EmptyListDeletesSessions()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var (target, targetSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "remove-all");

        var response = await SendAsync(HttpMethod.Put, $"/api/admin/staff/{target.Id}/roles", new { roleCodes = Array.Empty<string>() }, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertSessionIsGuestAsync(targetSession);
    }

    [Fact]
    // Không ai tự sửa role hoặc tự khóa chính mình, kể cả admin.
    public async Task SelfModification_IsRejected()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var roleResponse = await SendAsync(HttpMethod.Put, $"/api/admin/staff/{admin.Id}/roles", new { roleCodes = new[] { "sales" } }, adminSession);
        var lockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{admin.Id}/lock", body: null, adminSession);

        await RegisterTests.AssertErrorAsync(roleResponse, HttpStatusCode.BadRequest, "CANNOT_MODIFY_SELF", "Không thể tự sửa đổi role của chính mình.");
        await RegisterTests.AssertErrorAsync(lockResponse, HttpStatusCode.BadRequest, "CANNOT_MODIFY_SELF", "Không thể tự khóa tài khoản của chính mình.");
    }

    [Fact]
    // Người không phải admin không hạ quyền, không khóa và không mở khóa được admin.
    public async Task NonAdmin_CannotTouchAdmin()
    {
        var (_, session) = await CreateStaffManagerSessionAsync([Permissions.StaffManage]);
        var (targetAdmin, _) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "target-admin");

        var demoteResponse = await SendAsync(HttpMethod.Put, $"/api/admin/staff/{targetAdmin.Id}/roles", new { roleCodes = Array.Empty<string>() }, session);
        var lockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{targetAdmin.Id}/lock", body: null, session);
        var unlockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{targetAdmin.Id}/unlock", body: null, session);

        await RegisterTests.AssertErrorAsync(demoteResponse, HttpStatusCode.Forbidden, "FORBIDDEN", "Chỉ quản trị viên (admin) mới có quyền sửa đổi quyền của một admin khác.");
        await RegisterTests.AssertErrorAsync(lockResponse, HttpStatusCode.Forbidden, "FORBIDDEN", "Chỉ quản trị viên (admin) mới có quyền khóa tài khoản của một admin.");
        await RegisterTests.AssertErrorAsync(unlockResponse, HttpStatusCode.Forbidden, "FORBIDDEN", "Chỉ quản trị viên (admin) mới có quyền mở khóa tài khoản của một admin.");
    }

    [Fact]
    // Người quản lý nhân viên không thao tác được với nhân viên có quyền mình không có, kể cả nhân viên đã bị khóa.
    public async Task NonAdmin_CannotManageTargetWithStrongerPermissions()
    {
        var (_, session) = await CreateStaffManagerSessionAsync([Permissions.StaffManage]);
        var (target, _) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "stronger-target");

        var lockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{target.Id}/lock", body: null, session);
        var replaceResponse = await SendAsync(HttpMethod.Put, $"/api/admin/staff/{target.Id}/roles", new { roleCodes = Array.Empty<string>() }, session);
        await LockDirectlyAsync(target.Id);
        var relockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{target.Id}/lock", body: null, session);
        var unlockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{target.Id}/unlock", body: null, session);

        // Kiểm tra quyền chạy trước nhánh "đã khóa". Thông báo nêu quyền thiếu đầu tiên theo thứ tự chữ cái.
        const string expectedMessage = "Bạn không có quyền quản lý nhân viên sở hữu quyền \"ai-usage:read\".";
        await RegisterTests.AssertErrorAsync(lockResponse, HttpStatusCode.Forbidden, "FORBIDDEN", expectedMessage);
        await RegisterTests.AssertErrorAsync(replaceResponse, HttpStatusCode.Forbidden, "FORBIDDEN", expectedMessage);
        await RegisterTests.AssertErrorAsync(relockResponse, HttpStatusCode.Forbidden, "FORBIDDEN", expectedMessage);
        await RegisterTests.AssertErrorAsync(unlockResponse, HttpStatusCode.Forbidden, "FORBIDDEN", expectedMessage);
    }

    [Fact]
    // Người quản lý nhân viên khóa và mở khóa được nhân viên có tập con quyền của mình.
    public async Task NonAdmin_LocksAndUnlocksTargetWithSubsetPermissions()
    {
        var (_, session) = await CreateStaffManagerSessionAsync([Permissions.StaffManage, Permissions.OrdersRead]);
        var weakRole = await IdentityTestData.CreateRoleAsync(_database, [Permissions.OrdersRead]);
        var (target, targetSession) = await IdentityTestData.CreateStaffSessionAsync(_database, weakRole.Code, "weak-target");

        var lockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{target.Id}/lock", body: null, session);
        await AssertSessionIsGuestAsync(targetSession);
        var unlockResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{target.Id}/unlock", body: null, session);

        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(unlockResponse);
        Assert.Equal("Đã mở khóa tài khoản nhân viên.", document.RootElement.GetProperty("data").GetProperty("message").GetString());
    }

    [Fact]
    // Khóa nhân viên xóa phiên ngay và ghi nhật ký. Khóa lại người đã bị khóa không ghi thêm nhật ký.
    public async Task Lock_DeletesSessionsWritesAuditAndIsIdempotent()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var (target, targetSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "lock-target");

        var firstResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{target.Id}/lock", body: null, adminSession);
        var secondResponse = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{target.Id}/lock", body: null, adminSession);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(firstResponse);
        Assert.Equal("Đã khóa tài khoản nhân viên.", document.RootElement.GetProperty("data").GetProperty("message").GetString());
        await AssertSessionIsGuestAsync(targetSession);
        await using var identity = _database.CreateIdentityContext();
        var audit = await identity.AuditLogs.SingleAsync(log => log.TargetId == target.Id && log.Action == "staff.lock");
        Assert.Equal(admin.Id, audit.ActorId);
    }

    [Fact]
    // Id không thuộc nhân viên nhận 404. Route nhân viên không thao tác được trên tài khoản khách.
    public async Task Lock_CustomerIdReturns404()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var customer = await IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail("not-staff"), UserKind.Customer, "customer");

        var response = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{customer.Id}/lock", body: null, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.NotFound, "USER_NOT_FOUND", "Không tìm thấy tài khoản nhân viên.");
    }

    [Fact]
    // Gỡ quyền admin hoặc khóa admin cuối cùng còn hoạt động bị chặn. Hệ thống luôn còn người quản trị.
    public async Task LastActiveAdmin_CannotBeDemotedOrLocked()
    {
        var (lastAdmin, _) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "last-admin");
        await LockEveryOtherAdminAsync(lastAdmin.Id);
        // Actor admin không có trong DB. Qua API, người gọi luôn là một admin hoạt động khác nên nhánh này chỉ gọi thẳng được.
        var outsideAdmin = new Actor
        {
            AnonymousId = Guid.NewGuid().ToString(),
            UserId = Guid.NewGuid().ToString(),
            Kind = ActorKind.Staff,
            Roles = ["admin"],
            Permissions = Permissions.All,
            Consent = ConsentState.AllDisabled,
        };

        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<StaffManagementHandler>();
        var demote = await Assert.ThrowsAsync<AppException>(() => handler.AssignRolesAsync(outsideAdmin, lastAdmin.Id, ["sales"], CancellationToken.None));
        var lockAttempt = await Assert.ThrowsAsync<AppException>(() => handler.LockAsync(outsideAdmin, lastAdmin.Id, CancellationToken.None));

        Assert.Equal("LAST_ADMIN_PROTECTED", demote.Code);
        Assert.Equal("Không thể gỡ quyền admin của quản trị viên cuối cùng còn hoạt động.", demote.Message);
        Assert.Equal("LAST_ADMIN_PROTECTED", lockAttempt.Code);
        Assert.Equal("Không thể khóa quản trị viên cuối cùng còn hoạt động.", lockAttempt.Message);
    }

    [Fact]
    // Hai admin cuối cùng gỡ quyền admin của nhau cùng lúc. Giao dịch `Serializable` giữ lại ít nhất một admin.
    public async Task TwoLastAdmins_DemotingEachOtherConcurrentlyKeepsOneAdmin()
    {
        var (firstAdmin, firstSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "race-admin-a");
        var (secondAdmin, secondSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "race-admin-b");
        await LockEveryOtherAdminAsync(firstAdmin.Id, secondAdmin.Id);

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Put, $"/api/admin/staff/{secondAdmin.Id}/roles", new { roleCodes = new[] { "sales" } }, firstSession),
            SendAsync(HttpMethod.Put, $"/api/admin/staff/{firstAdmin.Id}/roles", new { roleCodes = new[] { "sales" } }, secondSession));

        // Đúng một request thành công. Request còn lại nhận 409 hoặc 400, tùy thời điểm xung đột.
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, response => response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);
        await using var identity = _database.CreateIdentityContext();
        var activeAdmins = await identity.UserRoles.CountAsync(value =>
            value.Role!.Code == "admin" && value.User!.LockedAt == null && value.User.DeletedAt == null);
        Assert.Equal(1, activeAdmins);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? sessionToken)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(method, path, body, sessionToken is null ? null : $"pk_session={sessionToken}"));
    }

    // Tạo người quản lý nhân viên không phải admin với bộ quyền cho trước.
    private async Task<(User User, string SessionToken)> CreateStaffManagerSessionAsync(IReadOnlyList<string> permissions)
    {
        var role = await IdentityTestData.CreateRoleAsync(_database, permissions);
        return await IdentityTestData.CreateStaffSessionAsync(_database, role.Code, "staff-manager");
    }

    private async Task AssertSessionIsGuestAsync(string sessionToken)
    {
        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, "/api/me", $"pk_session={sessionToken}"));
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("guest", document.RootElement.GetProperty("data").GetProperty("kind").GetString());
    }

    private async Task LockDirectlyAsync(string userId)
    {
        await using var identity = _database.CreateIdentityContext();
        await identity.Users.Where(value => value.Id == userId).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.LockedAt, DateTime.UtcNow));
    }

    // Khóa mọi admin khác trong DB test dùng chung. Test kiểm soát được số admin còn hoạt động.
    private async Task LockEveryOtherAdminAsync(params string[] keptAdminIds)
    {
        await using var identity = _database.CreateIdentityContext();
        await identity.Users
            .Where(user => user.Roles.Any(role => role.Role!.Code == "admin") && !keptAdminIds.Contains(user.Id) && user.LockedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LockedAt, DateTime.UtcNow));
    }
}
