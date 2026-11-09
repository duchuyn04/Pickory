using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Sessions;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra endpoint POST /api/admin/support/sessions/{id}/assignment cho nhận phiên (SD-06).
[Collection(SupportDeskCollection.Name)]
public sealed class AdminSupportSessionClaimEndpointTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
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

    private static string AssignmentPath(string sessionId) =>
        $"/api/admin/support/sessions/{sessionId}/assignment";

    [Fact]
    // Nhân viên bán hàng không có quyền conversations:reply nên phải nhận lỗi 403.
    public async Task Claim_Sales_Returns403()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SalesCode, "claim-sales");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, AssignmentPath("dummy-id"), body: null, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // Chặn nhân viên thiếu quyền để đảm bảo phân quyền tác vụ hỗ trợ.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Mã phiên không tồn tại trong hệ thống phải trả về lỗi 404.
    public async Task Claim_NonExistentSession_Returns404NotFound()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "claim-not-found");

        var nonExistentId = Guid.NewGuid().ToString();
        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, AssignmentPath(nonExistentId), body: null, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // Kiểm tra mã lỗi 404 và mã lỗi đặc thù SUPPORT_SESSION_NOT_FOUND.
        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, SupportDeskErrorCodes.SessionNotFound);
    }

    [Fact]
    // Phiên hỗ trợ đã kết thúc phải trả về 409 kể cả khi phiên đã có người nhận.
    public async Task Claim_ClosedSession_Returns409Closed()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "claim-closed");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        // Tạo phiên đã kết thúc và đã từng có nhân viên nhận trước đó.
        var closedSession = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, TestMoment.UtcDateTime.AddMinutes(-30), handoffInHours: true,
            staffId: staff.Id, claimedAt: TestMoment.UtcDateTime.AddMinutes(-28),
            closedAt: TestMoment.UtcDateTime.AddMinutes(-10));

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, AssignmentPath(closedSession.Id), body: null, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // Ưu tiên kiểm tra ClosedAt trước StaffId để báo đúng trạng thái phiên đã kết thúc.
        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.Conflict, SupportDeskErrorCodes.SessionClosed);
    }

    [Fact]
    // Nhận phiên thành công trả 201, cập nhật StaffId, ClaimedAt và ghi đúng 1 dòng audit.
    public async Task Claim_Success_Returns201_UpdatesSession_AndRecordsAudit()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "claim-success");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, TestMoment.UtcDateTime.AddMinutes(-5), handoffInHours: true);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, AssignmentPath(session.Id), body: null, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // Kiểm tra mã phản hồi 201 Created.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var data = document!.RootElement.GetProperty("data");
        Assert.Equal(session.Id, data.GetProperty("sessionId").GetString());
        Assert.Equal(staff.Id, data.GetProperty("staffId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("claimedAt").GetString()));

        // Kiểm tra database lưu đúng nhân viên và mốc thời gian nhận phiên.
        await using (var context = database.CreateSupportDeskContext())
        {
            var updated = await context.Sessions.SingleAsync(s => s.Id == session.Id);
            Assert.Equal(staff.Id, updated.StaffId);
            Assert.Equal(TestMoment.UtcDateTime, updated.ClaimedAt);

            // Kiểm tra tin nhắn hệ thống thông báo khách được tạo trong hội thoại.
            var systemMessage = await context.Messages
                .SingleOrDefaultAsync(m => m.ConversationId == conversation.Id && m.Role == SupportMessageRole.System);
            Assert.NotNull(systemMessage);
            Assert.Equal(SupportSystemMessages.SessionClaimed, systemMessage.Body);
            Assert.Null(systemMessage.Action);
        }

        // Kiểm tra bản ghi nhật ký kiểm toán trong module identity.
        await using (var identityContext = database.CreateIdentityContext())
        {
            var audit = await identityContext.AuditLogs
                .SingleOrDefaultAsync(log => log.TargetType == "support_session" && log.TargetId == session.Id);
            Assert.NotNull(audit);
            Assert.Equal(staff.Id, audit.ActorId);
            Assert.Equal("support.session.claim", audit.Action);
        }
    }

    [Fact]
    // Hai nhân viên gửi nhận phiên song song: đúng 1 người 201, 1 người 409 ALREADY_CLAIMED.
    public async Task Claim_ConcurrentRequests_OnlyOneSucceeds()
    {
        var (staff1, token1) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "claim-conc-1");
        var (staff2, token2) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "claim-conc-2");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, TestMoment.UtcDateTime.AddMinutes(-5), handoffInHours: true);

        var req1 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, AssignmentPath(session.Id), body: null, $"pk_session={token1}");
        var req2 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, AssignmentPath(session.Id), body: null, $"pk_session={token2}");

        // Gửi hai request song song qua Task.WhenAll để kiểm tra tranh chấp an toàn.
        var task1 = _client.SendAsync(req1);
        var task2 = _client.SendAsync(req2);
        await Task.WhenAll(task1, task2);

        var res1 = await task1;
        var res2 = await task2;

        var statuses = new[] { res1.StatusCode, res2.StatusCode };
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);

        var conflictRes = res1.StatusCode == HttpStatusCode.Conflict ? res1 : res2;
        await SupportConversationTestKit.AssertErrorAsync(
            conflictRes, HttpStatusCode.Conflict, SupportDeskErrorCodes.SessionAlreadyClaimed);

        // Database chỉ có đúng 1 dòng nhật ký audit và đúng 1 tin nhắn hệ thống.
        await using (var context = database.CreateSupportDeskContext())
        {
            var systemMessages = await context.Messages
                .Where(m => m.ConversationId == conversation.Id && m.Role == SupportMessageRole.System)
                .ToListAsync();
            Assert.Single(systemMessages);
        }

        await using (var identityContext = database.CreateIdentityContext())
        {
            var audits = await identityContext.AuditLogs
                .Where(log => log.TargetType == "support_session" && log.TargetId == session.Id)
                .ToListAsync();
            Assert.Single(audits);
        }
    }

    [Fact]
    // Khi transaction bị lỗi (audit writer ném lỗi), thay đổi được quay lui toàn bộ.
    public async Task Claim_FailingAudit_RollsBackSessionAndMessage()
    {
        // Dựng host riêng có audit writer cố ý ném lỗi để kiểm tra quay lui transaction.
        using var failingHost = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
        {
            services.RemoveAll<IAuditLogWriter>();
            services.AddSingleton<IAuditLogWriter, ThrowingAuditLogWriter>();
        });
        using var failingClient = failingHost.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "claim-fail-audit");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, TestMoment.UtcDateTime.AddMinutes(-5), handoffInHours: true);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, AssignmentPath(session.Id), body: null, $"pk_session={token}");
        var response = await failingClient.SendAsync(request);

        // Phản hồi phải trả về lỗi máy chủ nội bộ 500 do ngoại lệ chưa xử lý.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Kiểm tra database không lưu StaffId, ClaimedAt và không có tin hệ thống nào.
        await using (var context = database.CreateSupportDeskContext())
        {
            var unchangedSession = await context.Sessions.SingleAsync(s => s.Id == session.Id);
            Assert.Null(unchangedSession.StaffId);
            Assert.Null(unchangedSession.ClaimedAt);

            var systemMessages = await context.Messages
                .Where(m => m.ConversationId == conversation.Id && m.Role == SupportMessageRole.System)
                .ToListAsync();
            Assert.Empty(systemMessages);
        }

        // Kiểm tra không có dòng nhật ký audit nào được lưu lại.
        await using (var identityContext = database.CreateIdentityContext())
        {
            var audits = await identityContext.AuditLogs
                .Where(log => log.TargetType == "support_session" && log.TargetId == session.Id)
                .ToListAsync();
            Assert.Empty(audits);
        }
    }

    // Lớp giả lập ghi nhật ký kiểm toán bị lỗi để kiểm tra tính năng quay lui giao dịch.
    private sealed class ThrowingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Cố ý ném lỗi audit từ kiểm thử để kiểm tra rollback.");
    }
}
