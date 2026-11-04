using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra endpoint GET /api/admin/support/sessions cho hàng chờ CSKH (SD-06).
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminSupportSessionQueueEndpointTests(
    PickoryWebApplicationFactory factory,
    IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string QueuePath = "/api/admin/support/sessions";
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
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    [Fact]
    // Nhân viên bán hàng không có quyền conversations:read nên phải nhận lỗi 403.
    public async Task Get_Sales_Returns403()
    {
        // Tạo phiên đăng nhập với role sales không có quyền đọc hội thoại.
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SalesCode, "queue-sales-get");

        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=waiting", $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // Chặn truy cập trái quyền để bảo vệ dữ liệu CSKH.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Nhân viên CSKH có quyền conversations:read nên nhận kết quả 200.
    public async Task Get_Support_Returns200()
    {
        // Tạo phiên đăng nhập nhân viên support có quyền đọc hội thoại.
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "queue-support-get");

        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=waiting", $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // Cho phép truy cập khi nhân viên có đủ quyền hạn.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?view=")]
    [InlineData("?view=invalid")]
    // Tham số view là bắt buộc và chỉ nhận waiting hoặc mine, sai sẽ nhận 400.
    public async Task Get_InvalidOrMissingView_Returns400ValidationError(string queryString)
    {
        // Tạo phiên đăng nhập nhân viên hợp lệ để gọi API.
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "queue-invalid-view");

        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}{queryString}", $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // Kiểm tra mã trạng thái trả về đúng 400 Bad Request.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Kiểm tra chi tiết lỗi chứa mã VALIDATION_ERROR và trường view.
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var error = document!.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var details = error.GetProperty("details").EnumerateArray();
        Assert.Contains(details, detail => detail.GetProperty("key").GetString() == "view");
    }

    [Fact]
    // Đoạn trích lấy tin nhắn đầu tiên của khách trong phiên và bỏ qua tin hệ thống.
    public async Task Get_Preview_TakesFirstCustomerMessageOfSession_ExcludesSystemAndPriorMessages()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "queue-preview-test");

        var anonId = Guid.NewGuid().ToString();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, anonId);

        // Tin nhắn của phiên cũ trước thời điểm chuyển giao không được đưa vào preview.
        var priorMessageAt = TestMoment.UtcDateTime.AddHours(-2);
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversation.Id, "Tin nhắn cũ trước handoff", priorMessageAt, SupportMessageRole.Customer);

        // Tạo phiên chờ mới bắt đầu lúc T - 100 giây.
        var handoffAt = TestMoment.UtcDateTime.AddSeconds(-100);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, handoffAt, handoffInHours: true);

        // Tin nhắn đầu tiên của khách trong phiên này bắt đầu lúc handoffAt.
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversation.Id, "Tin nhắn khách mở phiên hợp lệ", handoffAt, SupportMessageRole.Customer);

        // Tin nhắn hệ thống ngay sau đó phải bị bỏ qua khi tính preview.
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversation.Id, "Hệ thống thông báo", handoffAt.AddMilliseconds(1), SupportMessageRole.System);

        // Tin nhắn tiếp theo của khách không được chọn làm preview.
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversation.Id, "Tin nhắn sau của khách", handoffAt.AddSeconds(10), SupportMessageRole.Customer);
        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=waiting", $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var items = document!.RootElement.GetProperty("data").EnumerateArray().ToList();
        var matched = items.Single(item => item.GetProperty("id").GetString() == session.Id);

        // Đoạn trích phải khớp chính xác tin nhắn đầu tiên của khách trong phiên.
        Assert.Equal("Tin nhắn khách mở phiên hợp lệ", matched.GetProperty("preview").GetString());
    }

    [Fact]
    // Đoạn trích của từng phiên chỉ lấy tin từ mốc handoffAt của chính phiên đó khi có nhiều phiên chờ.
    public async Task Get_Preview_FiltersMessagesByHandoffAtPerSession_WhenMultipleSessionsExist()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "queue-preview-multi");

        var conversationA = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString());
        var conversationB = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString());

        // Phiên A mở lúc T - 10 phút để kéo cận dưới earliestHandoff về T - 10 phút.
        var sessionAHandoffAt = TestMoment.UtcDateTime.AddMinutes(-10);
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversationA, sessionAHandoffAt, handoffInHours: true);
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversationA.Id, "Tin nhắn phiên A", sessionAHandoffAt, SupportMessageRole.Customer);

        // Hội thoại B có tin nhắn lúc T - 5 phút trước khi phiên B mở.
        var messageBeforeSessionBAt = TestMoment.UtcDateTime.AddMinutes(-5);
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversationB.Id, "Tin nhắn cũ trước khi mở phiên B", messageBeforeSessionBAt, SupportMessageRole.Customer);

        // Phiên B mở lúc T - 1 phút kèm tin nhắn mở phiên tại thời điểm đó.
        var sessionBHandoffAt = TestMoment.UtcDateTime.AddMinutes(-1);
        var sessionB = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationB, sessionBHandoffAt, handoffInHours: true);
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversationB.Id, "Tin nhắn mở phiên B", sessionBHandoffAt, SupportMessageRole.Customer);
        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=waiting", $"pk_session={token}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var items = doc!.RootElement.GetProperty("data").EnumerateArray().ToList();
        var itemB = items.Single(i => i.GetProperty("id").GetString() == sessionB.Id);

        // Đoạn trích của phiên B phải là tin nhắn lúc T - 1 phút, không phải tin lúc T - 5 phút.
        Assert.Equal("Tin nhắn mở phiên B", itemB.GetProperty("preview").GetString());
    }

    [Fact]
    // Số giây chờ tính đúng cho phiên chưa nhận và phiên đã có người nhận.
    public async Task Get_WaitingSeconds_CalculatedCorrectly_ForUnclaimedAndClaimedSessions()
    {
        // Tạo hai nhân viên CSKH để gán và kiểm tra phiên.
        var (staff1, token1) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "queue-wait-staff1");

        var anonId1 = Guid.NewGuid().ToString();
        var conversationUnclaimed = await SupportConversationTestKit.SeedConversationAsync(database, anonId1);

        // Phiên chưa nhận bắt đầu từ 95 giây trước, thời gian chờ là 95 giây.
        var handoffAt1 = TestMoment.UtcDateTime.AddSeconds(-95);
        var session1 = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationUnclaimed, handoffAt1, handoffInHours: true);

        var anonId2 = Guid.NewGuid().ToString();
        var conversationClaimed = await SupportConversationTestKit.SeedConversationAsync(database, anonId2);

        // Phiên đã nhận sau 40 giây chờ thì số giây chờ dừng ở 40 giây.
        var handoffAt2 = TestMoment.UtcDateTime.AddSeconds(-100);
        var claimedAt2 = handoffAt2.AddSeconds(40);
        var session2 = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationClaimed, handoffAt2, handoffInHours: true, staffId: staff1.Id, claimedAt: claimedAt2);

        // Kiểm tra danh sách hàng chờ waiting với phiên chưa nhận.
        var reqWaiting = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=waiting", $"pk_session={token1}");
        var resWaiting = await _client.SendAsync(reqWaiting);
        var docWaiting = await resWaiting.Content.ReadFromJsonAsync<JsonDocument>();
        var item1 = docWaiting!.RootElement.GetProperty("data").EnumerateArray()
            .Single(i => i.GetProperty("id").GetString() == session1.Id);
        Assert.Equal(95, item1.GetProperty("waitingSeconds").GetInt64());

        // Kiểm tra danh sách phiên của tôi mine với phiên đã nhận.
        var reqMine = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=mine", $"pk_session={token1}");
        var resMine = await _client.SendAsync(reqMine);
        var docMine = await resMine.Content.ReadFromJsonAsync<JsonDocument>();
        var item2 = docMine!.RootElement.GetProperty("data").EnumerateArray()
            .Single(i => i.GetProperty("id").GetString() == session2.Id);
        Assert.Equal(40, item2.GetProperty("waitingSeconds").GetInt64());
    }

    [Fact]
    // Danh sách waiting loại bỏ phiên đã nhận và phiên đã đóng.
    public async Task Get_WaitingView_ExcludesClaimedAndClosedSessions()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "queue-filter-waiting");

        var anonId = Guid.NewGuid().ToString();
        var conversationWaiting = await SupportConversationTestKit.SeedConversationAsync(database, anonId);

        // Tạo phiên đang chờ chưa ai nhận.
        var waitingSession = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationWaiting, TestMoment.UtcDateTime.AddMinutes(-5), handoffInHours: true);

        var conversationClaimed = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        // Tạo phiên đã có nhân viên nhận.
        var claimedSession = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationClaimed, TestMoment.UtcDateTime.AddMinutes(-10), handoffInHours: true,
            staffId: staff.Id, claimedAt: TestMoment.UtcDateTime.AddMinutes(-8));

        var conversationClosed = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        // Tạo phiên đã kết thúc chưa ai nhận.
        var closedSession = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationClosed, TestMoment.UtcDateTime.AddMinutes(-15), handoffInHours: true,
            closedAt: TestMoment.UtcDateTime.AddMinutes(-12));

        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=waiting", $"pk_session={token}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var ids = doc!.RootElement.GetProperty("data").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString())
            .ToList();

        // Danh sách waiting chỉ chứa phiên chưa nhận và chưa đóng.
        Assert.Contains(waitingSession.Id, ids);
        Assert.DoesNotContain(claimedSession.Id, ids);
        Assert.DoesNotContain(closedSession.Id, ids);
    }

    [Fact]
    // Danh sách mine chỉ trả về phiên của người gọi và giữ lại transferNote.
    public async Task Get_MineView_ReturnsOnlySessionsOfCaller_AndMapsTransferNote()
    {
        var (staff1, token1) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "queue-mine-staff1");
        var (staff2, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "queue-mine-staff2");

        var conversationStaff1 = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        // Tạo phiên của staff1 có kèm ghi chú chuyển giao.
        var sessionStaff1 = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationStaff1, TestMoment.UtcDateTime.AddMinutes(-10), handoffInHours: true,
            staffId: staff1.Id, claimedAt: TestMoment.UtcDateTime.AddMinutes(-9));

        // Cập nhật ghi chú chuyển giao trực tiếp trong database để kiểm tra ánh xạ.
        await using (var context = database.CreateSupportDeskContext())
        {
            var entity = await context.Sessions.FindAsync(sessionStaff1.Id);
            entity!.TransferNote = "Khách cần hỗ trợ đổi size áo.";
            await context.SaveChangesAsync();
        }

        var conversationStaff2 = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        // Tạo phiên của staff2 để xác nhận staff1 không nhìn thấy.
        var sessionStaff2 = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationStaff2, TestMoment.UtcDateTime.AddMinutes(-10), handoffInHours: true,
            staffId: staff2.Id, claimedAt: TestMoment.UtcDateTime.AddMinutes(-8));

        var conversationClosedStaff1 = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        // Tạo phiên của staff1 nhưng đã kết thúc.
        var closedStaff1 = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationClosedStaff1, TestMoment.UtcDateTime.AddMinutes(-20), handoffInHours: true,
            staffId: staff1.Id, claimedAt: TestMoment.UtcDateTime.AddMinutes(-18),
            closedAt: TestMoment.UtcDateTime.AddMinutes(-5));
        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=mine", $"pk_session={token1}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var items = doc!.RootElement.GetProperty("data").EnumerateArray().ToList();
        var ids = items.Select(i => i.GetProperty("id").GetString()).ToList();

        // Danh sách mine chỉ trả về phiên đang mở của chính nhân viên gọi.
        Assert.Contains(sessionStaff1.Id, ids);
        Assert.DoesNotContain(sessionStaff2.Id, ids);
        Assert.DoesNotContain(closedStaff1.Id, ids);

        // Kiểm tra ánh xạ trường transferNote từ database lên response.
        var matched = items.Single(i => i.GetProperty("id").GetString() == sessionStaff1.Id);
        Assert.Equal("Khách cần hỗ trợ đổi size áo.", matched.GetProperty("transferNote").GetString());
    }

    [Fact]
    // Thứ tự sắp xếp đúng theo handoffAt tăng dần cho waiting và claimedAt tăng dần cho mine.
    public async Task Get_Sorting_FollowsHandoffAtForWaiting_AndClaimedAtForMine()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "queue-sorting");

        var conversationWaiting1 = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var conversationWaiting2 = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());

        var earlyMoment = TestMoment.UtcDateTime.AddMinutes(-10);
        var lateMoment = TestMoment.UtcDateTime.AddMinutes(-5);

        // Tạo hai phiên waiting với handoffAt khác nhau.
        var sessionW1 = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationWaiting1, earlyMoment, handoffInHours: true);
        var sessionW2 = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationWaiting2, lateMoment, handoffInHours: true);

        var reqW = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=waiting", $"pk_session={token}");
        var resW = await _client.SendAsync(reqW);
        var docW = await resW.Content.ReadFromJsonAsync<JsonDocument>();
        var idsW = docW!.RootElement.GetProperty("data").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString())
            .Where(id => id == sessionW1.Id || id == sessionW2.Id)
            .ToList();

        // Phiên có handoffAt sớm hơn phải đứng trước trong danh sách waiting.
        Assert.Equal(sessionW1.Id, idsW[0]);
        Assert.Equal(sessionW2.Id, idsW[1]);

        var conversationMine1 = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var conversationMine2 = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());

        // Tạo hai phiên mine với claimedAt khác nhau.
        var sessionM1 = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationMine1, earlyMoment, handoffInHours: true, staffId: staff.Id, claimedAt: earlyMoment);
        var sessionM2 = await SupportSessionTestKit.SeedSessionAsync(
            database, conversationMine2, earlyMoment, handoffInHours: true, staffId: staff.Id, claimedAt: lateMoment);
        var reqM = ActorHttp.CreateRequest(
            HttpMethod.Get, $"{QueuePath}?view=mine", $"pk_session={token}");
        var resM = await _client.SendAsync(reqM);
        var docM = await resM.Content.ReadFromJsonAsync<JsonDocument>();
        var idsM = docM!.RootElement.GetProperty("data").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString())
            .Where(id => id == sessionM1.Id || id == sessionM2.Id)
            .ToList();

        // Phiên được nhận sớm hơn phải đứng trước trong danh sách mine.
        Assert.Equal(sessionM1.Id, idsM[0]);
        Assert.Equal(sessionM2.Id, idsM[1]);
    }
}
