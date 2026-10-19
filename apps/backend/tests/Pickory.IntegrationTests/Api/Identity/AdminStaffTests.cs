using System.Net;
using System.Text.Json;
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

        // DB dùng chung có nhiều nhân viên, nên nhân viên của test không chắc nằm ở trang 1. Tìm theo email riêng của test.
        var salesResponse = await SendAsync(
            HttpMethod.Get, $"/api/admin/staff?search={Uri.EscapeDataString(sales.Email)}", body: null, adminSession);
        var customerResponse = await SendAsync(
            HttpMethod.Get, $"/api/admin/staff?search={Uri.EscapeDataString(customer.Email)}", body: null, adminSession);

        Assert.Equal(HttpStatusCode.OK, salesResponse.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(salesResponse);
        var staff = document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var salesRow = staff.Single(row => row.GetProperty("id").GetString() == sales.Id);
        Assert.Equal(["sales"], salesRow.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        Assert.Equal("staff", salesRow.GetProperty("kind").GetString());
        Assert.False(salesRow.TryGetProperty("passwordHash", out _));

        // Khách không nằm trong danh sách nhân viên, kể cả khi tìm đúng email của khách.
        using var customerDocument = await ActorHttp.ReadJsonAsync(customerResponse);
        var customerData = customerDocument.RootElement.GetProperty("data");
        Assert.Empty(customerData.GetProperty("items").EnumerateArray());
        Assert.Equal(0, customerData.GetProperty("total").GetInt32());
    }

    [Fact]
    // Response có `items`, `total`, `page`, `limit`. Giá trị mặc định là trang 1 và 20 dòng.
    public async Task ListStaff_ReturnsPagedEnvelope()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync(HttpMethod.Get, "/api/admin/staff", body: null, adminSession);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.GetProperty("items").ValueKind);
        Assert.True(data.GetProperty("total").GetInt32() >= 1);
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("limit").GetInt32());
    }

    // Marker chỉ gồm chữ thường và số, không dấu. Tìm kiếm trên DB dùng chung chỉ khớp nhân viên của test.
    private static string NewMarker() => Guid.NewGuid().ToString("N")[..12];

    // Tạo nhân viên có tên riêng. `password` null nghĩa là chưa nhận lời mời.
    private async Task<User> CreateStaffAsync(
        string prefix, string roleCode, string name, string? password = IdentityTestData.DefaultPassword, bool locked = false)
    {
        var staff = await IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail(prefix), UserKind.Staff, roleCode, password: password, locked: locked);
        await using var identity = _database.CreateIdentityContext();
        await identity.Users.Where(user => user.Id == staff.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Name, name));
        return staff;
    }

    private async Task<(List<JsonElement> Items, int Total)> ListStaffAsync(string sessionToken, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/admin/staff?{query}", body: null, sessionToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var data = document.RootElement.GetProperty("data");
        var items = data.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();

        // Mọi dòng của mọi truy vấn đều không có hash mật khẩu. Response liệt kê từng trường.
        Assert.All(items, item => Assert.False(item.TryGetProperty("passwordHash", out _)));
        return (items, data.GetProperty("total").GetInt32());
    }

    private static List<string> IdsOf(IEnumerable<JsonElement> items) =>
        items.Select(item => item.GetProperty("id").GetString()!).ToList();

    [Theory]
    [InlineData("nguyen")]
    [InlineData("NGUYEN")]
    [InlineData("Nguyễn")]
    // Tên lưu có dấu. Từ khóa không dấu, viết hoa hoặc có dấu đều tìm được.
    public async Task ListStaff_SearchIgnoresDiacriticsAndCaseByName(string keyword)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var marker = NewMarker();
        var staff = await CreateStaffAsync("diacritics", "sales", $"Nguyễn Văn {marker}");

        var (items, total) = await ListStaffAsync(adminSession, $"search={Uri.EscapeDataString($"{keyword} van {marker}")}");

        Assert.Equal([staff.Id], IdsOf(items));
        Assert.Equal(1, total);
    }

    [Fact]
    // Tìm theo email. Phần trước `@` chứa marker nên chỉ khớp nhân viên của test.
    public async Task ListStaff_SearchesByEmail()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var staff = await CreateStaffAsync("email-search", "sales", "Không liên quan");

        var (items, _) = await ListStaffAsync(adminSession, $"search={Uri.EscapeDataString(staff.Email.ToUpperInvariant())}");

        Assert.Equal([staff.Id], IdsOf(items));
    }

    private sealed record StatusFixture(User Active, User Invited, User LockedWithoutPassword, User LockedWithPassword, string Marker);

    // Bốn nhân viên cùng marker. Nhân viên khóa chưa có mật khẩu chỉ được thuộc `locked`.
    private async Task<StatusFixture> CreateStatusFixtureAsync()
    {
        var marker = NewMarker();
        var name = $"Trạng thái {marker}";
        var active = await CreateStaffAsync("st-active", "sales", name);
        var invited = await CreateStaffAsync("st-invited", "sales", name, password: null);
        var lockedWithoutPassword = await CreateStaffAsync("st-locked-a", "sales", name, password: null, locked: true);
        var lockedWithPassword = await CreateStaffAsync("st-locked-b", "sales", name, locked: true);
        return new StatusFixture(active, invited, lockedWithoutPassword, lockedWithPassword, marker);
    }

    [Fact]
    // `status=invited` chỉ trả nhân viên chưa khóa và chưa có mật khẩu.
    public async Task ListStaff_StatusInvitedReturnsOnlyUnlockedWithoutPassword()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var fixture = await CreateStatusFixtureAsync();

        var (items, total) = await ListStaffAsync(adminSession, $"search={fixture.Marker}&status=invited");

        Assert.Equal([fixture.Invited.Id], IdsOf(items));
        Assert.Equal(1, total);
    }

    [Fact]
    // `status=active` chỉ trả nhân viên chưa khóa và đã có mật khẩu.
    public async Task ListStaff_StatusActiveReturnsOnlyUnlockedWithPassword()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var fixture = await CreateStatusFixtureAsync();

        var (items, total) = await ListStaffAsync(adminSession, $"search={fixture.Marker}&status=active");

        Assert.Equal([fixture.Active.Id], IdsOf(items));
        Assert.Equal(1, total);
    }

    [Fact]
    // `status=locked` trả mọi nhân viên đã khóa, kể cả nhân viên chưa có mật khẩu. Nhân viên này không thuộc `invited`.
    public async Task ListStaff_StatusLockedIncludesLockedWithoutPassword()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var fixture = await CreateStatusFixtureAsync();

        var (items, total) = await ListStaffAsync(adminSession, $"search={fixture.Marker}&status=locked");

        Assert.Equal(
            new[] { fixture.LockedWithoutPassword.Id, fixture.LockedWithPassword.Id }.Order(StringComparer.Ordinal),
            IdsOf(items).Order(StringComparer.Ordinal));
        Assert.Equal(2, total);
    }

    [Fact]
    // Mỗi dòng có `status` theo cùng luật với bộ lọc. Nhân viên khóa chưa có mật khẩu là `locked`, không phải `invited`.
    public async Task ListStaff_EachRowHasStatusFollowingFilterRule()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var fixture = await CreateStatusFixtureAsync();

        var (items, _) = await ListStaffAsync(adminSession, $"search={fixture.Marker}");

        var statusById = items.ToDictionary(
            item => item.GetProperty("id").GetString()!, item => item.GetProperty("status").GetString());
        Assert.Equal(4, statusById.Count);
        Assert.Equal("active", statusById[fixture.Active.Id]);
        Assert.Equal("invited", statusById[fixture.Invited.Id]);
        Assert.Equal("locked", statusById[fixture.LockedWithoutPassword.Id]);
        Assert.Equal("locked", statusById[fixture.LockedWithPassword.Id]);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("invited")]
    [InlineData("locked")]
    // Luật của bộ lọc và luật của trường khớp nhau: mọi dòng lọc theo một giá trị mang đúng giá trị đó.
    public async Task ListStaff_FilteredRowsCarryTheFilteredStatus(string status)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var fixture = await CreateStatusFixtureAsync();

        var (items, total) = await ListStaffAsync(adminSession, $"search={fixture.Marker}&status={status}");

        Assert.NotEmpty(items);
        Assert.Equal(items.Count, total);
        Assert.All(items, item => Assert.Equal(status, item.GetProperty("status").GetString()));
    }

    [Fact]
    // `role=sales` chỉ trả nhân viên có role `sales`. Nhân viên nhiều role vẫn hiện đủ mã role, không chỉ role đã lọc.
    public async Task ListStaff_RoleFilterKeepsEveryRoleCodeOfMatchingStaff()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var marker = NewMarker();
        var name = $"Vai trò {marker}";
        var salesOnly = await CreateStaffAsync("role-sales", "sales", name);
        var supportOnly = await CreateStaffAsync("role-support", "support", name);
        var both = await CreateStaffAsync("role-both", "sales", name);
        await using (var identity = _database.CreateIdentityContext())
        {
            var supportRoleId = await identity.Roles.Where(role => role.Code == "support").Select(role => role.Id).SingleAsync();
            identity.UserRoles.Add(new UserRole { UserId = both.Id, RoleId = supportRoleId });
            await identity.SaveChangesAsync();
        }

        var (items, total) = await ListStaffAsync(adminSession, $"search={marker}&role=sales");

        Assert.Equal(new[] { salesOnly.Id, both.Id }.Order(StringComparer.Ordinal), IdsOf(items).Order(StringComparer.Ordinal));
        Assert.Equal(2, total);
        Assert.DoesNotContain(supportOnly.Id, IdsOf(items));
        var bothRow = items.Single(item => item.GetProperty("id").GetString() == both.Id);
        Assert.Equal(
            ["sales", "support"],
            bothRow.GetProperty("roles").EnumerateArray().Select(role => role.GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    // Mã role không tồn tại không phải lỗi. Danh sách rỗng và `total` bằng 0.
    public async Task ListStaff_UnknownRoleCodeReturnsEmptyList()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var (items, total) = await ListStaffAsync(adminSession, $"role=khong-ton-tai-{NewMarker()}");

        Assert.Empty(items);
        Assert.Equal(0, total);
    }

    [Theory]
    [InlineData("status=blocked", "status")]
    [InlineData("status=ACTIVE", "status")]
    [InlineData("status=active&status=locked", "status")]
    [InlineData("role=sales&role=support", "role")]
    [InlineData("search=a&search=b", "search")]
    // Giá trị lạ hoặc tham số lặp lại nhận 400 với đúng tên trường.
    public async Task ListStaff_InvalidFilterIsRejected(string query, string field)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");

        var response = await SendAsync(HttpMethod.Get, $"/api/admin/staff?{query}", body: null, adminSession);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Contains($"{field}:", error.GetProperty("message").GetString());
    }

    [Fact]
    // `total` đếm theo cùng điều kiện lọc. `limit` nhỏ hơn số dòng khớp chỉ cắt dòng trả về.
    public async Task ListStaff_TotalCountsFilteredRowsNotPageRows()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var marker = NewMarker();
        var name = $"Đếm {marker}";
        await CreateStaffAsync("count-a", "sales", name);
        await CreateStaffAsync("count-b", "sales", name);
        await CreateStaffAsync("count-c", "sales", name);
        await CreateStaffAsync("count-d", "sales", name, password: null);

        var (items, total) = await ListStaffAsync(adminSession, $"search={marker}&status=active&limit=1");

        Assert.Single(items);
        Assert.Equal(3, total);
    }

    [Fact]
    // Nhân viên cùng `CreatedAt` nằm ở các trang liền nhau. Thứ tự phụ theo `Id` giữ cho không lặp và không mất dòng.
    public async Task ListStaff_SameCreatedAtAcrossPages_NoDuplicateAndNoMissingRow()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var marker = NewMarker();
        var staffIds = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            staffIds.Add((await CreateStaffAsync($"tie-{index}", "sales", $"Cùng giờ {marker}")).Id);
        }

        // Mỗi `UPDATE` ghi bản dòng mới ở cuối bảng, nên cập nhật theo `Id` giảm dần đặt thứ tự lưu ngược với `Id`.
        // Truy vấn thiếu thứ tự phụ sẽ trả sai thứ tự `Id`.
        var sameInstant = DateTime.UtcNow.AddMinutes(-5);
        foreach (var staffId in staffIds.OrderByDescending(id => id, StringComparer.Ordinal))
        {
            await using var identity = _database.CreateIdentityContext();
            await identity.Users.Where(user => user.Id == staffId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.CreatedAt, sameInstant));
        }

        var collectedIds = new List<string>();
        foreach (var pageNumber in new[] { 1, 2, 3 })
        {
            var (items, total) = await ListStaffAsync(adminSession, $"search={marker}&limit=2&page={pageNumber}");
            Assert.Equal(6, total);
            collectedIds.AddRange(IdsOf(items));
        }

        Assert.Equal(staffIds.Order(StringComparer.Ordinal).ToList(), collectedIds);
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
        var lockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{admin.Id}", new { locked = true }, adminSession);

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
        var lockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{targetAdmin.Id}", new { locked = true }, session);
        var unlockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{targetAdmin.Id}", new { locked = false }, session);

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

        var lockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", new { locked = true }, session);
        var replaceResponse = await SendAsync(HttpMethod.Put, $"/api/admin/staff/{target.Id}/roles", new { roleCodes = Array.Empty<string>() }, session);
        await LockDirectlyAsync(target.Id);
        var relockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", new { locked = true }, session);
        var unlockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", new { locked = false }, session);

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

        var lockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", new { locked = true }, session);
        await AssertSessionIsGuestAsync(targetSession);
        var unlockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", new { locked = false }, session);

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

        var firstResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", new { locked = true }, adminSession);
        var secondResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", new { locked = true }, adminSession);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(firstResponse);
        Assert.Equal("Đã khóa tài khoản nhân viên.", document.RootElement.GetProperty("data").GetProperty("message").GetString());
        await AssertSessionIsGuestAsync(targetSession);
        await using var identity = _database.CreateIdentityContext();
        // Dòng phiên bị xóa khỏi DB. Kiểm trực tiếp vì tài khoản bị khóa cũng bị từ chối khi đọc phiên.
        Assert.Equal(0, await identity.Sessions.CountAsync(session => session.UserId == target.Id));
        var audit = await identity.AuditLogs.SingleAsync(log => log.TargetId == target.Id && log.Action == "staff.lock");
        Assert.Equal(admin.Id, audit.ActorId);
    }

    [Fact]
    // Id không thuộc nhân viên nhận 404. Route nhân viên không thao tác được trên tài khoản khách.
    public async Task Lock_CustomerIdReturns404()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var customer = await IdentityTestData.CreateUserAsync(_database, IdentityTestData.NewEmail("not-staff"), UserKind.Customer, "customer");

        var response = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{customer.Id}", new { locked = true }, adminSession);

        await RegisterTests.AssertErrorAsync(response, HttpStatusCode.NotFound, "USER_NOT_FOUND", "Không tìm thấy tài khoản nhân viên.");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"locked\":null}")]
    [InlineData("{\"locked\":\"false\"}")]
    [InlineData("{\"locked\":true,\"roleCodes\":[\"admin\"]}")]
    [InlineData("[]")]
    // Body thiếu `locked`, sai kiểu hoặc có trường lạ nhận 400. Nhân viên không bị khóa và phiên còn nguyên.
    public async Task PatchStaff_InvalidBodyReturns400AndChangesNothing(string rawBody)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var (target, _) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "patch-invalid");
        using var request = ActorHttp.CreateRequest(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", $"pk_session={adminSession}");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = new StringContent(rawBody, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ActorHttp.ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        await using var identity = _database.CreateIdentityContext();
        Assert.Null(await identity.Users.Where(user => user.Id == target.Id).Select(user => user.LockedAt).SingleAsync());
        Assert.Equal(1, await identity.Sessions.CountAsync(session => session.UserId == target.Id));
    }

    [Fact]
    // Mở khóa bằng `locked: false` xóa thời điểm khóa và ghi đúng một dòng nhật ký `staff.unlock`.
    public async Task PatchStaff_UnlockClearsLockedAtAndWritesOneAuditRow()
    {
        var (admin, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var (target, _) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "patch-unlock");

        var lockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", new { locked = true }, adminSession);
        await using (var afterLock = _database.CreateIdentityContext())
        {
            Assert.NotNull(await afterLock.Users.Where(user => user.Id == target.Id).Select(user => user.LockedAt).SingleAsync());
        }

        var unlockResponse = await SendAsync(HttpMethod.Patch, $"/api/admin/staff/{target.Id}", new { locked = false }, adminSession);

        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);
        await using var identity = _database.CreateIdentityContext();
        Assert.Null(await identity.Users.Where(user => user.Id == target.Id).Select(user => user.LockedAt).SingleAsync());
        var unlockAudit = await identity.AuditLogs.SingleAsync(log => log.TargetId == target.Id && log.Action == "staff.unlock");
        Assert.Equal(admin.Id, unlockAudit.ActorId);
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("unlock")]
    // Hai route cũ đặt theo hành động không còn đăng ký. Test tách đường dẫn và hành động để lệnh grep route cũ không báo nhầm.
    public async Task OldLockRoutes_AreNoLongerRegistered(string action)
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin");
        var (target, _) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "old-route");

        var response = await SendAsync(HttpMethod.Post, $"/api/admin/staff/{target.Id}/" + action, body: null, adminSession);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
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

        // Đúng một request thành công, request thua nhận 409 hoặc 400 khi hai giao dịch va nhau ở DB.
        // Mã 403 xảy ra khi request kia đã hạ quyền người gửi trước lúc kiểm quyền.
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, response => response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden);
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
