using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra hợp đồng API cho các ví dụ của admin-sessions.json và customer-api.json.
[Collection(SupportDeskCollection.Name)]
public sealed class AdminSupportSessionContractTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string FixtureFileName = "admin-sessions.json";
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
    // Hợp đồng hàng chờ waiting phải khớp hoàn toàn cấu trúc ví dụ hàng chờ.
    public async Task Contract_WaitingQueue_MatchesExample()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-waiting");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var handoffAt = TestMoment.UtcDateTime.AddSeconds(-95);
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, handoffAt, handoffInHours: true);
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversation.Id, "Đơn PK123 của mình giao tới đâu rồi?", handoffAt, SupportMessageRole.Customer);

        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, "/api/admin/support/sessions?view=waiting", $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // So khớp cấu trúc và kiểu dữ liệu của response với file mẫu hợp đồng.
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body, response.StatusCode, FixtureFileName, "hàng chờ");
    }

    [Fact]
    // Hợp đồng phiên của tôi sau chuyển giao phải khớp ví dụ phiên của tôi.
    public async Task Contract_MineQueue_MatchesExample()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-mine");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var handoffAt = TestMoment.UtcDateTime.AddMinutes(-20);
        var claimedAt = handoffAt.AddSeconds(40);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, handoffAt, handoffInHours: true, staffId: staff.Id, claimedAt: claimedAt);

        // Cập nhật TransferNote vào database để khớp với ví dụ hợp đồng.
        await using (var context = database.CreateSupportDeskContext())
        {
            var entity = await context.Sessions.FindAsync(session.Id);
            entity!.TransferNote = "Khách cần đổi size, đơn PK098 đã giao.";
            await context.SaveChangesAsync();
        }

        await SupportConversationTestKit.SeedMessageAsync(
            database, conversation.Id, "Mình muốn đổi size áo.", handoffAt, SupportMessageRole.Customer);

        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, "/api/admin/support/sessions?view=mine", $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // So khớp cấu trúc dữ liệu của response với ví dụ phiên của tôi sau khi đồng nghiệp chuyển.
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(
            body, response.StatusCode, FixtureFileName, "phiên của tôi sau khi đồng nghiệp chuyển");
    }

    [Fact]
    // Hợp đồng nhận phiên thành công và nhận phiên đã có người nhận phải khớp ví dụ.
    public async Task Contract_ClaimSessionAndAlreadyClaimed_MatchesExamples()
    {
        var (staff1, token1) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-claim-1");
        var (_, token2) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-claim-2");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, TestMoment.UtcDateTime.AddMinutes(-5), handoffInHours: true);

        // Gửi nhận phiên lần đầu tiên thành công trả 201 Created.
        var req1 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/assignment",
            body: null,
            $"pk_session={token1}");
        var res1 = await _client.SendAsync(req1);
        var body1 = JsonNode.Parse(await res1.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body1, res1.StatusCode, FixtureFileName, "nhận phiên");

        // Gửi nhận phiên lần thứ hai bởi nhân viên khác trả 409 Conflict đã có người nhận.
        var req2 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/assignment",
            body: null,
            $"pk_session={token2}");
        var res2 = await _client.SendAsync(req2);
        var body2 = JsonNode.Parse(await res2.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body2, res2.StatusCode, FixtureFileName, "nhận phiên đã có người nhận");
    }

    [Fact]
    // Hợp đồng gửi tin thành công và người không giữ phiên gửi tin khớp các ví dụ mẫu.
    public async Task Contract_SendMessageAndNotAssigned_MatchesExamples()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-msg-1");
        var (_, otherToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-msg-2");

        // Cập nhật tên hồ sơ nhân viên trong Identity DB.
        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = "Lan - CSKH";
            await identity.SaveChangesAsync();
        }

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, TestMoment.UtcDateTime.AddMinutes(-5), handoffInHours: true);
        session.StaffId = staff.Id;
        session.ClaimedAt = TestMoment.UtcDateTime.AddMinutes(-2);
        await using (var context = database.CreateSupportDeskContext())
        {
            context.Sessions.Update(session);
            await context.SaveChangesAsync();
        }

        // Nhân viên gửi tin thành công khớp ví dụ.
        var req1 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/messages",
            new { body = "Chào bạn, mình kiểm đơn PK123 ngay nhé." },
            $"pk_session={token}");
        var res1 = await _client.SendAsync(req1);
        var body1 = JsonNode.Parse(await res1.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body1, res1.StatusCode, FixtureFileName, "nhân viên gửi tin");

        // Người không giữ phiên gửi tin khớp ví dụ 403.
        var req2 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/messages",
            new { body = "Chào bạn." },
            $"pk_session={otherToken}");
        var res2 = await _client.SendAsync(req2);
        var body2 = JsonNode.Parse(await res2.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body2, res2.StatusCode, FixtureFileName, "người không giữ phiên gửi tin");
    }

    [Fact]
    // Hợp đồng lịch sử tin của hội thoại cho nhân viên khớp ví dụ mẫu.
    public async Task Contract_AdminConversationMessages_MatchesExample()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-hist-staff");

        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = "Lan - CSKH";
            await identity.SaveChangesAsync();
        }

        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, TestMoment.UtcDateTime.AddMinutes(-10), handoffInHours: true);
        session.StaffId = staff.Id;
        session.ClaimedAt = TestMoment.UtcDateTime.AddMinutes(-5);

        await using (var context = database.CreateSupportDeskContext())
        {
            context.Sessions.Update(session);
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.Customer,
                Body = "Đơn PK123 của mình giao tới đâu rồi?",
                SessionId = session.Id,
                CreatedAt = TestMoment.UtcDateTime.AddMinutes(-5),
            });
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.Staff,
                StaffId = staff.Id,
                Body = "Chào bạn, mình kiểm đơn PK123 ngay nhé.",
                SessionId = session.Id,
                CreatedAt = TestMoment.UtcDateTime.AddMinutes(-2),
            });
            await context.SaveChangesAsync();
        }

        var req = ActorHttp.CreateJsonRequest(
            HttpMethod.Get,
            $"/api/admin/support/conversations/{conversation.Id}/messages?limit=50",
            body: null,
            $"pk_session={token}");
        var res = await _client.SendAsync(req);
        var body = JsonNode.Parse(await res.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body, res.StatusCode, FixtureFileName, "lịch sử tin của hội thoại cho nhân viên");
    }

    [Fact]
    // Hợp đồng lịch sử tin phía khách khớp ví dụ trong customer-api.json.
    public async Task Contract_CustomerMessageHistory_MatchesExample()
    {
        var (staff, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-cust-staff");

        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = "Lan - CSKH";
            await identity.SaveChangesAsync();
        }

        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));

        await using (var context = database.CreateSupportDeskContext())
        {
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.Customer,
                Body = "Đơn PK123 của mình giao tới đâu rồi?",
                CreatedAt = TestMoment.UtcDateTime.AddMinutes(-10),
            });
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.System,
                Body = "Đã chuyển cho nhân viên CSKH. Thời gian chờ dự kiến khoảng 5 phút.",
                CreatedAt = TestMoment.UtcDateTime.AddMinutes(-10),
            });
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                Role = SupportMessageRole.Staff,
                StaffId = staff.Id,
                Body = "Chào bạn, mình kiểm đơn PK123 ngay nhé.",
                CreatedAt = TestMoment.UtcDateTime.AddMinutes(-5),
            });
            await context.SaveChangesAsync();
        }

        var req = SupportConversationTestKit.Get(
            $"/api/support/conversations/{conversation.Id}/messages?limit=50", cookie);
        var res = await _client.SendAsync(req);
        var body = JsonNode.Parse(await res.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body, res.StatusCode, "customer-api.json", "lịch sử tin, trang cuối");
    }

    [Fact]
    // Hợp đồng chuyển phiên thành công và chuyển cho người tạm vắng khớp ví dụ mẫu.
    public async Task Contract_TransferSessionAndUnavailable_MatchesExamples()
    {
        var (staff1, token1) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-transfer-1");
        var (staff2, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-transfer-2");
        var (staff3, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-transfer-3");

        // Đặt nhân viên 2 ở trạng thái available và nhân viên 3 ở trạng thái away.
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, staff2.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, staff3.Id, StaffPresenceStatus.Away, _clock.GetUtcNow());

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            TestMoment.UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: staff1.Id,
            claimedAt: TestMoment.UtcDateTime.AddMinutes(-5));

        // Chuyển cho người đang tạm vắng trả 409 SUPPORT_STAFF_UNAVAILABLE.
        var awayReq = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            $"/api/admin/support/sessions/{session.Id}/assignment",
            new { staffId = staff3.Id, note = (string?)null },
            $"pk_session={token1}");
        var awayRes = await _client.SendAsync(awayReq);
        var awayBody = JsonNode.Parse(await awayRes.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(awayBody, awayRes.StatusCode, FixtureFileName, "chuyển cho người đang tạm vắng");

        // Chuyển cho đồng nghiệp đang sẵn sàng trả 200 OK.
        var succReq = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            $"/api/admin/support/sessions/{session.Id}/assignment",
            new { staffId = staff2.Id, note = "Khách cần đổi size, đơn PK098 đã giao." },
            $"pk_session={token1}");
        var succRes = await _client.SendAsync(succReq);
        var succBody = JsonNode.Parse(await succRes.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(succBody, succRes.StatusCode, FixtureFileName, "chuyển phiên cho đồng nghiệp");
    }

    [Fact]
    // Hợp đồng kết thúc phiên khớp ví dụ mẫu trong admin-sessions.json.
    public async Task Contract_CloseSession_MatchesExample()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-close-1");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            TestMoment.UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: staff.Id,
            claimedAt: TestMoment.UtcDateTime.AddMinutes(-5));

        var req = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch,
            $"/api/admin/support/sessions/{session.Id}",
            new { status = "closed" },
            $"pk_session={token}");
        var res = await _client.SendAsync(req);
        var body = JsonNode.Parse(await res.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body, res.StatusCode, FixtureFileName, "kết thúc phiên");
    }
}
