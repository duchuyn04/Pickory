using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-10: `GET`, `POST`, `PATCH`, `DELETE /api/admin/support/canned-responses[/{id}]`.
// Mẫu cá nhân cần `conversations:reply`. Mẫu dùng chung cần `support-settings:manage`.
[Collection(SupportDeskCollection.Name)]
public sealed class AdminCannedResponseEndpointTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string Path = "/api/admin/support/canned-responses";
    private const string PersonalTakenMessage = "Bạn đã có mẫu dùng phím tắt này.";
    private const string SharedTakenMessage = "Đã có mẫu dùng chung dùng phím tắt này.";

    // Mốc 10:00 ngày 2026-11-03 theo giờ Việt Nam. `updatedAt` khóa theo đồng hồ giả.
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await DeleteAllCannedResponsesAsync();
    }

    // ---- Quyền cơ bản ----

    [Fact]
    // Role sales không có quyền nào của CSKH, nên mọi route mẫu trả 403.
    public async Task AllRoutes_Sales_Return403()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SalesCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, Path, cookie, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, Path, cookie, NewBody("camon"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Patch, $"{Path}/x", cookie, new { body = "a" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Delete, $"{Path}/x", cookie, null)).StatusCode);
    }

    // ---- GET ----

    [Fact]
    // Danh sách gồm mẫu cá nhân của người gọi và mọi mẫu dùng chung, sắp theo `shortcut`. Mẫu của người khác không có.
    public async Task Get_Support_ReturnsOwnAndSharedSortedWithEditableFlags()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var (other, _) = await CreateSessionAsync(DefaultRoles.SupportCode);
        await SeedAsync("zeta", ownerId: support.Id, shared: false);
        await SeedAsync("alpha", ownerId: null, shared: true);
        await SeedAsync("mid_1", ownerId: support.Id, shared: false);
        await SeedAsync("hidden", ownerId: other.Id, shared: false);

        var response = await SendAsync(HttpMethod.Get, Path, cookie, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await SupportConversationTestKit.ReadDataAsync(response)).EnumerateArray().ToList();
        Assert.Equal(["alpha", "mid_1", "zeta"], items.Select(item => item.GetProperty("shortcut").GetString()));
        // Role support không có `support-settings:manage`, nên mẫu dùng chung chỉ đọc.
        Assert.Equal([false, true, true], items.Select(item => item.GetProperty("editable").GetBoolean()));
        Assert.Equal([true, false, false], items.Select(item => item.GetProperty("shared").GetBoolean()));
    }

    [Fact]
    // Người có cả hai quyền thấy mẫu dùng chung với `editable = true`.
    public async Task Get_AdminWithManage_SharedIsEditable()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        await SeedAsync("shared-one", ownerId: null, shared: true);

        var response = await SendAsync(HttpMethod.Get, Path, cookie, null);

        var items = (await SupportConversationTestKit.ReadDataAsync(response)).EnumerateArray().ToList();
        var shared = Assert.Single(items, item => item.GetProperty("shortcut").GetString() == "shared-one");
        Assert.True(shared.GetProperty("editable").GetBoolean());
    }

    [Fact]
    // Người chỉ có `support-settings:manage` thiếu `conversations:reply`, nên `GET` trả 403.
    public async Task Get_StaffWithOnlySettingsManage_Returns403()
    {
        var (_, cookie) = await CreateManageOnlySessionAsync();

        var response = await SendAsync(HttpMethod.Get, Path, cookie, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Mẫu cá nhân ----

    [Fact]
    // Tạo mẫu cá nhân: 201, `ownerId` là người gọi, thân được cắt khoảng trắng và không ghi audit.
    public async Task Post_PersonalTemplate_CreatesRowForCallerWithoutAudit()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var response = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "baohanh", body = "  Bảo hành 12 tháng.  ", shared = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(["id", "shortcut", "body", "shared", "editable"], data.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Bảo hành 12 tháng.", data.GetProperty("body").GetString());
        Assert.True(data.GetProperty("editable").GetBoolean());
        var stored = await FindAsync(data.GetProperty("id").GetString()!);
        Assert.Equal(support.Id, stored!.OwnerId);
        Assert.False(stored.Shared);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, stored.CreatedAt);
        Assert.Equal(0, await CountAuditAsync(support.Id));
    }

    [Fact]
    // `shared` có thể vắng mặt. Khi đó mẫu là mẫu cá nhân.
    public async Task Post_WithoutSharedField_CreatesPersonalTemplate()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var response = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "no-shared", body = "x" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False((await SupportConversationTestKit.ReadDataAsync(response)).GetProperty("shared").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Camon")]
    [InlineData("ca mon")]
    [InlineData("/camon")]
    [InlineData("ca.mon")]
    [InlineData("camơn")]
    [InlineData("abc\n")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    // `shortcut` sai luật trả 400 với khóa `shortcut`. Chữ hoa bị từ chối, server không tự đổi.
    public async Task Post_InvalidShortcut_Returns400(string shortcut)
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var response = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut, body = "x", shared = false });

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(response, "shortcut");
        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    // Biên hợp lệ: 32 ký tự, và đủ bốn nhóm ký tự `a-z`, `0-9`, `-`, `_`.
    public async Task Post_ShortcutBoundaries_Returns201()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var longest = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = new string('a', 32), body = "x", shared = false });
        var mixed = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "a-b_9", body = "x", shared = false });

        Assert.Equal(HttpStatusCode.Created, longest.StatusCode);
        Assert.Equal(HttpStatusCode.Created, mixed.StatusCode);
    }

    [Fact]
    // `shortcut` thiếu hoặc không phải chuỗi trả 400 thay vì lỗi 500.
    public async Task Post_ShortcutMissingOrNumber_Returns400()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var missing = await SendAsync(HttpMethod.Post, Path, cookie, new { body = "x", shared = false });
        var number = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = 5, body = "x", shared = false });

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(missing, "shortcut");
        await SupportDeskHttpAssertions.AssertValidationErrorAsync(number, "shortcut");
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    // Thân rỗng hoặc chỉ có khoảng trắng trả 400 với khóa `body`.
    public async Task Post_EmptyBody_Returns400(string body)
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var response = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "camon", body, shared = false });

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(response, "body");
    }

    [Fact]
    // Biên thân: 2000 ký tự sau khi cắt hợp lệ, 2001 ký tự trả 400. Khoảng trắng hai đầu không tính vào độ dài.
    public async Task Post_BodyLengthBoundary_Allows2000AndRejects2001()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var ok = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "ok", body = " " + new string('b', 2000) + " ", shared = false });
        var tooLong = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "long", body = new string('b', 2001), shared = false });

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        await SupportDeskHttpAssertions.AssertValidationErrorAsync(tooLong, "body");
    }

    [Fact]
    // Thân không phải chuỗi và `shared` không phải boolean đều trả 400.
    public async Task Post_WrongJsonKinds_Returns400()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var bodyNumber = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "a", body = 12, shared = false });
        var sharedText = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "a", body = "x", shared = "yes" });

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(bodyNumber, "body");
        await SupportDeskHttpAssertions.AssertValidationErrorAsync(sharedText, "shared");
    }

    [Fact]
    // Phím tắt trùng trong mẫu cá nhân của cùng người trả 409. Người khác dùng cùng phím tắt vẫn được.
    public async Task Post_DuplicatePersonalShortcut_Returns409ButOtherUserMayReuse()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var (_, otherCookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        await SendAsync(HttpMethod.Post, Path, cookie, NewBody("kiemdon"));

        var duplicate = await SendAsync(HttpMethod.Post, Path, cookie, NewBody("kiemdon"));
        var otherUser = await SendAsync(HttpMethod.Post, Path, otherCookie, NewBody("kiemdon"));

        await AssertTakenAsync(duplicate, PersonalTakenMessage);
        Assert.Equal(HttpStatusCode.Created, otherUser.StatusCode);
    }

    [Fact]
    // Mẫu cá nhân được cùng phím tắt với mẫu dùng chung.
    public async Task Post_PersonalShortcutSameAsSharedOne_Returns201()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        await SeedAsync("camon", ownerId: null, shared: true);

        var response = await SendAsync(HttpMethod.Post, Path, cookie, NewBody("camon"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    // `PATCH` mẫu của chính mình: chỉ trường được gửi thay đổi, `updatedAt` theo đồng hồ, `shared` không đổi được.
    public async Task Patch_OwnPersonal_ChangesOnlySentFieldsAndNeverShared()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var seeded = await SeedAsync("old", ownerId: support.Id, shared: false, body: "Nội dung cũ");
        _clock.Advance(TimeSpan.FromMinutes(5));

        var bodyOnly = await SendAsync(HttpMethod.Patch, $"{Path}/{seeded.Id}", cookie, new { body = " Nội dung mới ", shared = true });
        var afterBody = await FindAsync(seeded.Id);
        var shortcutOnly = await SendAsync(HttpMethod.Patch, $"{Path}/{seeded.Id}", cookie, new { shortcut = "new" });
        var afterShortcut = await FindAsync(seeded.Id);

        Assert.Equal(HttpStatusCode.OK, bodyOnly.StatusCode);
        Assert.Equal("old", afterBody!.Shortcut);
        Assert.Equal("Nội dung mới", afterBody.Body);
        Assert.False(afterBody.Shared);
        Assert.Equal(support.Id, afterBody.OwnerId);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, afterBody.UpdatedAt);
        Assert.Equal(HttpStatusCode.OK, shortcutOnly.StatusCode);
        Assert.Equal("new", afterShortcut!.Shortcut);
        Assert.Equal("Nội dung mới", afterShortcut.Body);
        Assert.Equal(0, await CountAuditAsync(support.Id));
    }

    [Fact]
    // Mẫu cá nhân của người khác: `PATCH` và `DELETE` đều trả 404 và dòng giữ nguyên, không lộ là mẫu có tồn tại.
    public async Task PatchAndDelete_OtherUsersPersonal_Return404AndKeepRow()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var (other, _) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var theirs = await SeedAsync("theirs", ownerId: other.Id, shared: false, body: "gốc");

        var patch = await SendAsync(HttpMethod.Patch, $"{Path}/{theirs.Id}", cookie, new { body = "bị sửa" });
        var delete = await SendAsync(HttpMethod.Delete, $"{Path}/{theirs.Id}", cookie, null);

        await SupportConversationTestKit.AssertErrorAsync(patch, HttpStatusCode.NotFound, "CANNED_RESPONSE_NOT_FOUND");
        await SupportConversationTestKit.AssertErrorAsync(delete, HttpStatusCode.NotFound, "CANNED_RESPONSE_NOT_FOUND");
        Assert.Equal("gốc", (await FindAsync(theirs.Id))!.Body);
    }

    [Fact]
    // `id` không tồn tại trả 404 `CANNED_RESPONSE_NOT_FOUND` cho cả `PATCH` và `DELETE`.
    public async Task PatchAndDelete_UnknownId_Return404()
    {
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var unknown = Guid.NewGuid();

        var patch = await SendAsync(HttpMethod.Patch, $"{Path}/{unknown}", cookie, new { body = "x" });
        var delete = await SendAsync(HttpMethod.Delete, $"{Path}/{unknown}", cookie, null);

        await SupportConversationTestKit.AssertErrorAsync(patch, HttpStatusCode.NotFound, "CANNED_RESPONSE_NOT_FOUND");
        await SupportConversationTestKit.AssertErrorAsync(delete, HttpStatusCode.NotFound, "CANNED_RESPONSE_NOT_FOUND");
    }

    [Fact]
    // `DELETE` mẫu của chính mình trả 204 không có body và xóa dòng.
    public async Task Delete_OwnPersonal_Returns204AndRemovesRow()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var seeded = await SeedAsync("bye", ownerId: support.Id, shared: false);

        var response = await SendAsync(HttpMethod.Delete, $"{Path}/{seeded.Id}", cookie, null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Null(await FindAsync(seeded.Id));
        Assert.Equal(0, await CountAuditAsync(support.Id));
    }

    [Fact]
    // Đổi `shortcut` sang giá trị đã có trong mẫu cá nhân của mình trả 409 và dòng giữ nguyên.
    public async Task Patch_ShortcutCollidingWithOwnOtherTemplate_Returns409()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        await SeedAsync("taken", ownerId: support.Id, shared: false);
        var mine = await SeedAsync("free", ownerId: support.Id, shared: false);

        var response = await SendAsync(HttpMethod.Patch, $"{Path}/{mine.Id}", cookie, new { shortcut = "taken" });

        await AssertTakenAsync(response, PersonalTakenMessage);
        Assert.Equal("free", (await FindAsync(mine.Id))!.Shortcut);
    }

    [Fact]
    // Body `PATCH` rỗng, hoặc chỉ có trường `shared`, trả 400 vì không có gì để đổi.
    public async Task Patch_NothingToChange_Returns400()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var mine = await SeedAsync("mine", ownerId: support.Id, shared: false);

        var empty = await SendAsync(HttpMethod.Patch, $"{Path}/{mine.Id}", cookie, new { });
        var onlyShared = await SendAsync(HttpMethod.Patch, $"{Path}/{mine.Id}", cookie, new { shared = true });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, onlyShared.StatusCode);
        Assert.False((await FindAsync(mine.Id))!.Shared);
    }

    [Fact]
    // Trường `PATCH` đã gửi phải hợp lệ như lúc tạo.
    public async Task Patch_InvalidFields_Returns400()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var mine = await SeedAsync("mine", ownerId: support.Id, shared: false);

        var badShortcut = await SendAsync(HttpMethod.Patch, $"{Path}/{mine.Id}", cookie, new { shortcut = "Mine" });
        var emptyBody = await SendAsync(HttpMethod.Patch, $"{Path}/{mine.Id}", cookie, new { body = " " });
        var nullShortcut = await SendAsync(HttpMethod.Patch, $"{Path}/{mine.Id}", cookie, new { shortcut = (string?)null });

        await SupportDeskHttpAssertions.AssertValidationErrorAsync(badShortcut, "shortcut");
        await SupportDeskHttpAssertions.AssertValidationErrorAsync(emptyBody, "body");
        await SupportDeskHttpAssertions.AssertValidationErrorAsync(nullShortcut, "shortcut");
    }

    // ---- Mẫu dùng chung ----

    [Fact]
    // Thiếu `support-settings:manage`: tạo, sửa, xóa mẫu dùng chung đều trả 403 và dữ liệu không đổi.
    public async Task SharedWrites_SupportWithoutManage_Return403()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var shared = await SeedAsync("shared", ownerId: null, shared: true, body: "gốc");

        var create = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "new", body = "x", shared = true });
        var patch = await SendAsync(HttpMethod.Patch, $"{Path}/{shared.Id}", cookie, new { body = "sửa" });
        var delete = await SendAsync(HttpMethod.Delete, $"{Path}/{shared.Id}", cookie, null);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(1, await CountRowsAsync());
        Assert.Equal("gốc", (await FindAsync(shared.Id))!.Body);
        Assert.Equal(0, await CountAuditAsync(support.Id));
    }

    [Fact]
    // Người chỉ có `support-settings:manage` (không có `conversations:reply`) vẫn tạo, sửa, xóa được mẫu dùng chung.
    // Mỗi thay đổi ghi đúng 1 dòng audit với `before` và `after`.
    public async Task SharedWrites_StaffWithOnlyManage_SucceedAndWriteOneAuditRowEach()
    {
        var (manager, cookie) = await CreateManageOnlySessionAsync();

        var create = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "team", body = "Xin chào", shared = true });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await SupportConversationTestKit.ReadDataAsync(create);
        var id = created.GetProperty("id").GetString()!;
        Assert.True(created.GetProperty("shared").GetBoolean());
        Assert.True(created.GetProperty("editable").GetBoolean());
        Assert.Null((await FindAsync(id))!.OwnerId);
        Assert.Equal(1, await CountAuditAsync(manager.Id));

        var patch = await SendAsync(HttpMethod.Patch, $"{Path}/{id}", cookie, new { body = "Xin chào bạn" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(2, await CountAuditAsync(manager.Id));

        var delete = await SendAsync(HttpMethod.Delete, $"{Path}/{id}", cookie, null);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Null(await FindAsync(id));

        var audits = await ReadAuditAsync(manager.Id);
        Assert.Equal(
            ["support.canned_response.create", "support.canned_response.update", "support.canned_response.delete"],
            audits.Select(audit => audit.Action));
        Assert.All(audits, audit =>
        {
            Assert.Equal("support_canned_response", audit.TargetType);
            Assert.Equal(id, audit.TargetId);
        });
        // Bản ghi audit lưu JSON `null` khi không có giá trị, nên kiểm loại giá trị thay vì tham chiếu `null`.
        Assert.Equal(JsonValueKind.Null, audits[0].Before?.RootElement.ValueKind ?? JsonValueKind.Null);
        Assert.Equal("Xin chào", audits[0].After!.RootElement.GetProperty("body").GetString());
        Assert.Equal("Xin chào", audits[1].Before!.RootElement.GetProperty("body").GetString());
        Assert.Equal("Xin chào bạn", audits[1].After!.RootElement.GetProperty("body").GetString());
        Assert.Equal("Xin chào bạn", audits[2].Before!.RootElement.GetProperty("body").GetString());
        Assert.Equal(JsonValueKind.Null, audits[2].After?.RootElement.ValueKind ?? JsonValueKind.Null);
    }

    [Fact]
    // Người chỉ có `support-settings:manage` không tạo được mẫu cá nhân vì thiếu `conversations:reply`.
    public async Task Post_PersonalByStaffWithOnlyManage_Returns403()
    {
        var (_, cookie) = await CreateManageOnlySessionAsync();

        var response = await SendAsync(HttpMethod.Post, Path, cookie, NewBody("mine"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Người có `support-settings:manage` vẫn không sửa được mẫu cá nhân của người khác: trả 404, không phải 403.
    public async Task Patch_ManagerOnOtherUsersPersonal_Returns404()
    {
        var (_, cookie) = await CreateManageOnlySessionAsync();
        var (other, _) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var theirs = await SeedAsync("theirs", ownerId: other.Id, shared: false);

        var response = await SendAsync(HttpMethod.Patch, $"{Path}/{theirs.Id}", cookie, new { body = "x" });

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "CANNED_RESPONSE_NOT_FOUND");
    }

    [Fact]
    // Phím tắt trùng trong tập mẫu dùng chung trả 409 với thông báo riêng. Dữ liệu và audit không đổi.
    public async Task Post_DuplicateSharedShortcut_Returns409WithSharedMessage()
    {
        var (manager, cookie) = await CreateManageOnlySessionAsync();
        await SeedAsync("camon", ownerId: null, shared: true);

        var response = await SendAsync(HttpMethod.Post, Path, cookie, new { shortcut = "camon", body = "x", shared = true });

        await AssertTakenAsync(response, SharedTakenMessage);
        Assert.Equal(1, await CountRowsAsync());
        Assert.Equal(0, await CountAuditAsync(manager.Id));
    }

    [Fact]
    // Mẫu dùng chung đổi `shortcut` sang giá trị đã có trong tập dùng chung trả 409. Không ghi audit.
    public async Task Patch_SharedShortcutColliding_Returns409WithoutAudit()
    {
        var (manager, cookie) = await CreateManageOnlySessionAsync();
        await SeedAsync("taken", ownerId: null, shared: true);
        var free = await SeedAsync("free", ownerId: null, shared: true);

        var response = await SendAsync(HttpMethod.Patch, $"{Path}/{free.Id}", cookie, new { shortcut = "taken" });

        await AssertTakenAsync(response, SharedTakenMessage);
        Assert.Equal(0, await CountAuditAsync(manager.Id));
    }

    [Fact]
    // Hai mẫu cá nhân của hai người, cùng một mẫu dùng chung, đều có cùng phím tắt. Cả ba tồn tại song song.
    public async Task SameShortcut_PersonalOfTwoUsersAndShared_Coexist()
    {
        var (_, supportCookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var (_, adminCookie) = await CreateSessionAsync(DefaultRoles.AdminCode);
        var (_, managerCookie) = await CreateManageOnlySessionAsync();

        var first = await SendAsync(HttpMethod.Post, Path, supportCookie, NewBody("chung"));
        var second = await SendAsync(HttpMethod.Post, Path, adminCookie, NewBody("chung"));
        var shared = await SendAsync(HttpMethod.Post, Path, managerCookie, new { shortcut = "chung", body = "x", shared = true });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, shared.StatusCode);
    }

    // ---- Đua ----

    [Fact]
    // Hai request tạo cùng `shortcut` cho cùng người chạy chồng lên nhau: đúng 1 thành công, request kia 409, không có 500.
    // `SaveChanges` của cả hai request bị giữ ở một rào chắn tới khi cả hai cùng tới, nên hai lệnh `INSERT` luôn chồng nhau.
    public async Task Post_TwoConcurrentRequestsSameShortcut_OneCreatedOneConflict()
    {
        var gate = new ConcurrentSaveGate(participants: 2);
        using var racingHost = SupportDeskTestKit.CreateHostWithClock(
            factory,
            _clock,
            services => services.ConfigureDbContext<Pickory.Modules.SupportDesk.Persistence.SupportDeskDbContext>(
                options => options.AddInterceptors(gate)));
        using var racingClient = racingHost.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (_, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);

        var requests = new[]
        {
            racingClient.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, Path, NewBody("dua"), cookie)),
            racingClient.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, Path, NewBody("dua"), cookie)),
        };
        var responses = await Task.WhenAll(requests);

        // Cổng phải xác nhận cả hai `SaveChanges` đã tới trước khi một bên được ghi. Nếu không, test chưa chứng minh sự chồng nhau.
        Assert.Equal(2, gate.ArrivedCount);
        var statuses = responses.Select(response => response.StatusCode).Order().ToArray();
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], statuses);
        var loser = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertTakenAsync(loser, PersonalTakenMessage);
        Assert.Equal(1, await CountRowsAsync());
    }

    [Fact]
    // Hai request tạo mẫu dùng chung cùng `shortcut` chạy chồng nhau: đúng 1 thành công, request kia 409 với thông báo mẫu dùng chung.
    // Request thua rollback transaction, nên chỉ có 1 dòng mẫu và 1 dòng audit `create`.
    public async Task Post_TwoConcurrentSharedRequestsSameShortcut_OneCreatedOneConflictAndOneAuditRow()
    {
        var gate = new ConcurrentSaveGate(participants: 2);
        using var racingHost = SupportDeskTestKit.CreateHostWithClock(
            factory,
            _clock,
            services => services.ConfigureDbContext<Pickory.Modules.SupportDesk.Persistence.SupportDeskDbContext>(
                options => options.AddInterceptors(gate)));
        using var racingClient = racingHost.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (manager, cookie) = await CreateManageOnlySessionAsync();
        var sharedBody = new { shortcut = "dua-chung", body = "Nội dung mẫu.", shared = true };

        var responses = await Task.WhenAll(
            racingClient.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, Path, sharedBody, cookie)),
            racingClient.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Post, Path, sharedBody, cookie)));

        Assert.Equal(2, gate.ArrivedCount);
        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order().ToArray());
        await AssertTakenAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Conflict), SharedTakenMessage);
        Assert.Equal(1, await CountRowsAsync());
        Assert.Equal(1, await CountAuditAsync(manager.Id));
    }

    [Fact]
    // `DELETE` mẫu cá nhân mà dòng đã bị request khác xóa giữa lúc đọc và ghi trả 404, không phải 500.
    public async Task Delete_PersonalRowRemovedBetweenReadAndWrite_Returns404()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var seeded = await SeedAsync("mat", ownerId: support.Id, shared: false);
        using var racingHost = CreateHostDeletingRowBeforeSave(seeded.Id);
        using var racingClient = racingHost.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await racingClient.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"{Path}/{seeded.Id}", body: null, cookie));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "CANNED_RESPONSE_NOT_FOUND");
    }

    [Fact]
    // `PATCH` mẫu cá nhân mà dòng đã bị request khác xóa giữa lúc đọc và ghi trả 404, không phải 500.
    public async Task Patch_PersonalRowRemovedBetweenReadAndWrite_Returns404()
    {
        var (support, cookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var seeded = await SeedAsync("mat", ownerId: support.Id, shared: false);
        using var racingHost = CreateHostDeletingRowBeforeSave(seeded.Id);
        using var racingClient = racingHost.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await racingClient.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Patch, $"{Path}/{seeded.Id}", new { body = "mới" }, cookie));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "CANNED_RESPONSE_NOT_FOUND");
    }

    [Fact]
    // Mẫu dùng chung bị xóa giữa lúc đọc và ghi: `DELETE` trả 404 và transaction rollback, nên không có dòng audit mới.
    public async Task Delete_SharedRowRemovedBetweenReadAndWrite_Returns404WithoutAudit()
    {
        var (manager, cookie) = await CreateManageOnlySessionAsync();
        var seeded = await SeedAsync("mat-chung", ownerId: null, shared: true);
        using var racingHost = CreateHostDeletingRowBeforeSave(seeded.Id);
        using var racingClient = racingHost.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await racingClient.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Delete, $"{Path}/{seeded.Id}", body: null, cookie));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "CANNED_RESPONSE_NOT_FOUND");
        Assert.Equal(0, await CountAuditAsync(manager.Id));
    }

    // Host mà mọi lần lưu sửa hoặc xóa mẫu `rowId` đều bị xóa dòng đó trước bằng `DbContext` khác.
    private WebApplicationFactory<Program> CreateHostDeletingRowBeforeSave(string rowId)
    {
        var interceptor = new DeleteRowBeforeSaveInterceptor(database.CreateSupportDeskContext, rowId);
        return SupportDeskTestKit.CreateHostWithClock(
            factory,
            _clock,
            services => services.ConfigureDbContext<Pickory.Modules.SupportDesk.Persistence.SupportDeskDbContext>(
                options => options.AddInterceptors(interceptor)));
    }

    // ---- Hợp đồng ----

    [Fact]
    // Response khớp mọi ví dụ trong `admin-canned-responses.json`.
    public async Task Responses_MatchTheContractExamples()
    {
        var (support, supportCookie) = await CreateSessionAsync(DefaultRoles.SupportCode);
        var (_, managerCookie) = await CreateManageOnlySessionAsync();
        await SeedAsync("camon", ownerId: null, shared: true, body: "Cảm ơn {ten_khach} đã liên hệ Pickory.");
        var personal = await SeedAsync("kiemdon", ownerId: support.Id, shared: false, body: "Mình kiểm đơn {ma_don} giúp bạn nhé.");

        AssertContract(await SendAsync(HttpMethod.Get, Path, supportCookie, null), "danh sách mẫu");

        var created = await SendAsync(HttpMethod.Post, Path, supportCookie, new { shortcut = "baohanh", body = "Sản phẩm được bảo hành 12 tháng.", shared = false });
        AssertContract(created, "tạo mẫu cá nhân");
        var createdId = (await SupportConversationTestKit.ReadDataAsync(created)).GetProperty("id").GetString();

        var patched = await SendAsync(HttpMethod.Patch, $"{Path}/{createdId}", supportCookie, new { body = "Sản phẩm được bảo hành chính hãng 12 tháng." });
        AssertContract(patched, "sửa mẫu");

        var duplicate = await SendAsync(HttpMethod.Post, Path, supportCookie, new { shortcut = personal.Shortcut, body = "Mình xem đơn giúp bạn.", shared = false });
        AssertContract(duplicate, "trùng phím tắt");

        var sharedDuplicate = await SendAsync(HttpMethod.Post, Path, managerCookie, new { shortcut = "camon", body = "Cảm ơn bạn.", shared = true });
        AssertContract(sharedDuplicate, "trùng phím tắt mẫu dùng chung");
    }

    // ---- Hàm dùng chung ----

    private static void AssertContract(HttpResponseMessage response, string exampleName)
    {
        var body = JsonNode.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        SupportDeskContract.AssertMatches(body, response.StatusCode, "admin-canned-responses.json", exampleName);
    }

    private static async Task AssertTakenAsync(HttpResponseMessage response, string expectedMessage)
    {
        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.Conflict, "CANNED_SHORTCUT_TAKEN");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedMessage, document.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    private static object NewBody(string shortcut) => new { shortcut, body = "Nội dung mẫu.", shared = false };

    private async Task<(Pickory.Modules.Identity.Entities.User User, string Cookie)> CreateSessionAsync(string roleCode)
    {
        var (user, token) = await IdentityTestData.CreateStaffSessionAsync(database, roleCode, "canned");
        return (user, $"pk_session={token}");
    }

    // Vai trò chỉ có `support-settings:manage`, thiếu `conversations:reply`.
    private async Task<(Pickory.Modules.Identity.Entities.User User, string Cookie)> CreateManageOnlySessionAsync()
    {
        var role = await IdentityTestData.CreateRoleAsync(database, [Permissions.SupportSettingsManage]);
        return await CreateSessionAsync(role.Code);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string cookie, object? body)
    {
        return _client.SendAsync(ActorHttp.CreateJsonRequest(method, path, body, cookie));
    }

    private async Task<CannedResponse> SeedAsync(string shortcut, string? ownerId, bool shared, string body = "Nội dung mẫu.")
    {
        await using var context = database.CreateSupportDeskContext();
        var row = new CannedResponse
        {
            OwnerId = ownerId,
            Shortcut = shortcut,
            Body = body,
            Shared = shared,
            CreatedAt = TestMoment.UtcDateTime,
            UpdatedAt = TestMoment.UtcDateTime,
        };
        context.CannedResponses.Add(row);
        await context.SaveChangesAsync();
        return row;
    }

    private async Task<CannedResponse?> FindAsync(string id)
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.CannedResponses.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id);
    }

    private async Task<int> CountRowsAsync()
    {
        await using var context = database.CreateSupportDeskContext();
        return await context.CannedResponses.CountAsync();
    }

    private async Task DeleteAllCannedResponsesAsync()
    {
        await using var context = database.CreateSupportDeskContext();
        await context.CannedResponses.ExecuteDeleteAsync();
    }

    // Mỗi test tạo người dùng mới, nên đếm theo `ActorId` chỉ thấy audit của chính test đó.
    private async Task<int> CountAuditAsync(string actorId)
    {
        await using var identity = database.CreateIdentityContext();
        return await identity.AuditLogs.CountAsync(log => log.ActorId == actorId && log.TargetType == "support_canned_response");
    }

    private async Task<List<Pickory.Modules.Identity.Entities.AuditLog>> ReadAuditAsync(string actorId)
    {
        await using var identity = database.CreateIdentityContext();
        return await identity.AuditLogs.AsNoTracking()
            .Where(log => log.ActorId == actorId && log.TargetType == "support_canned_response")
            .OrderBy(log => log.CreatedAt)
            .ToListAsync();
    }
}
