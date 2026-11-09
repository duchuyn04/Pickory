using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Modules.SupportDesk.Sessions;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra endpoint `PUT /api/admin/support/sessions/{id}/assignment` cho chuyển phiên (SD-08).
[Collection(SupportDeskCollection.Name)]
public sealed class AdminSupportSessionTransferEndpointTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;
    private SupportEventHub _staffHub = null!;

    public async Task InitializeAsync()
    {
        // Xóa bảng trực trước mỗi test để tránh ảnh hưởng trạng thái từ test trước.
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _staffHub = _host.Services.GetRequiredService<SupportEventHub>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        // Dọn dẹp trạng thái trực và dữ liệu hội thoại sau khi hoàn tất test.
        await SupportDeskTestKit.ClearPresenceAsync(database);
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    private static string AssignmentPath(string sessionId) =>
        $"/api/admin/support/sessions/{sessionId}/assignment";

    [Fact]
    // Nhân viên bán hàng không có quyền conversations:reply phải nhận 403 Forbidden.
    public async Task Transfer_WithoutPermission_Returns403()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SalesCode, "transfer-sales");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath("dummy-id"),
            new { staffId = "staff-0002", note = "Ghi chú nội bộ." },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Ghi chú hợp lệ.", "staffId")]
    [InlineData("   ", "Ghi chú hợp lệ.", "staffId")]
    [InlineData("same-as-caller", "Ghi chú hợp lệ.", "staffId")]
    // Kiểm tra các trường hợp vi phạm tính hợp lệ của tham số đầu vào.
    public async Task Transfer_InvalidPayload_Returns400(string staffIdInput, string noteInput, string expectedErrorKey)
    {
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-invalid-val");
        var targetStaffId = staffIdInput == "same-as-caller" ? caller.Id : staffIdInput;

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath("dummy-id"),
            new { staffId = targetStaffId, note = noteInput },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var code = document.RootElement.GetProperty("error").GetProperty("code").GetString();
        Assert.Equal("VALIDATION_ERROR", code);
        var details = document.RootElement.GetProperty("error").GetProperty("details");
        var hasKey = details.EnumerateArray().Any(d => d.GetProperty("key").GetString() == expectedErrorKey);
        Assert.True(hasKey);
    }

    [Fact]
    // Ghi chú quá 500 ký tự phải trả lỗi 400 VALIDATION_ERROR.
    public async Task Transfer_NoteTooLong_Returns400()
    {
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-long-note");
        var longNote = new string('A', 501);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath("dummy-id"),
            new { staffId = "other-staff-id", note = longNote },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Phiên không tồn tại phải trả 404 SUPPORT_SESSION_NOT_FOUND.
    public async Task Transfer_SessionNotFound_Returns404()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-not-found");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(Guid.NewGuid().ToString()),
            new { staffId = "staff-receiver", note = "Ghi chú." },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SupportDeskErrorCodes.SessionNotFound, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Phiên đã đóng phải trả 409 SUPPORT_SESSION_CLOSED.
    public async Task Transfer_SessionClosed_Returns409()
    {
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-closed");
        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: caller.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5),
            closedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-1));

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(session.Id),
            new { staffId = "staff-receiver", note = "Ghi chú." },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SupportDeskErrorCodes.SessionClosed, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Người gọi không giữ phiên phải nhận 403 `SUPPORT_SESSION_NOT_ASSIGNED`.
    public async Task Transfer_NotAssignedToCaller_Returns403()
    {
        var (otherStaff, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-owner");
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-caller");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: otherStaff.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(session.Id),
            new { staffId = "staff-receiver", note = "Ghi chú." },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SupportDeskErrorCodes.SessionNotAssigned, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Phiên chưa ai nhận mà có người chuyển phải trả 403 `SUPPORT_SESSION_NOT_ASSIGNED`.
    public async Task Transfer_UnclaimedSession_Returns403()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-unclaimed");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: null,
            claimedAt: null);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(session.Id),
            new { staffId = "staff-receiver", note = "Ghi chú." },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SupportDeskErrorCodes.SessionNotAssigned, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Người nhận không ở trạng thái available phải trả 409 SUPPORT_STAFF_UNAVAILABLE.
    public async Task Transfer_ReceiverNotAvailable_Returns409()
    {
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-caller-away");
        var (receiver, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-receiver-away");

        // Đặt người nhận ở trạng thái away.
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, receiver.Id, StaffPresenceStatus.Away, _clock.GetUtcNow());

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: caller.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(session.Id),
            new { staffId = receiver.Id, note = "Ghi chú chuyển." },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SUPPORT_STAFF_UNAVAILABLE", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Người nhận không có dòng trực trong bảng presence phải trả 409 SUPPORT_STAFF_UNAVAILABLE.
    public async Task Transfer_ReceiverNoPresenceRow_Returns409()
    {
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-caller-nopresence");
        var (receiver, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-receiver-nopresence");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: caller.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(session.Id),
            new { staffId = receiver.Id, note = "Ghi chú chuyển." },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SUPPORT_STAFF_UNAVAILABLE", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Chuyển phiên thành công cập nhật phiên, ghi 1 dòng audit và note không vào support_messages.
    public async Task Transfer_Success_UpdatesSessionAndWritesAuditAndNoteNotInMessages()
    {
        var (caller, callerToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-succ-caller");
        var (receiver, receiverToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-succ-receiver");

        // Đặt người nhận ở trạng thái available.
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, receiver.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: caller.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        const string noteText = "Khách cần đổi size áo thun đơn PK098.";
        var transferMoment = _clock.GetUtcNow().UtcDateTime;

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(session.Id),
            new { staffId = receiver.Id, note = noteText },
            $"pk_session={callerToken}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resBody = await response.Content.ReadFromJsonAsync<ApiResponse<SupportSessionClaimResponse>>();
        Assert.NotNull(resBody);
        Assert.Equal(session.Id, resBody.Data.SessionId);
        Assert.Equal(receiver.Id, resBody.Data.StaffId);
        Assert.Equal(transferMoment, resBody.Data.ClaimedAt);

        // Kiểm tra cơ sở dữ liệu đã cập nhật đầy đủ thông tin chuyển phiên.
        await using (var context = database.CreateSupportDeskContext())
        {
            var updatedSession = await context.Sessions.SingleAsync(s => s.Id == session.Id);
            Assert.Equal(receiver.Id, updatedSession.StaffId);
            Assert.Equal(transferMoment, updatedSession.ClaimedAt);
            Assert.Equal(noteText, updatedSession.TransferNote);
            Assert.Null(updatedSession.ClosedAt);

            // Kiểm tra ghi chú nội bộ không được lưu vào tin nhắn hội thoại.
            var noteInMessages = await context.Messages
                .AnyAsync(m => m.ConversationId == conversation.Id && m.Body.Contains("PK098"));
            Assert.False(noteInMessages);
        }

        // Kiểm tra đúng 1 bản ghi audit log support.session.transfer được ghi.
        await using (var identityContext = database.CreateIdentityContext())
        {
            var audits = await identityContext.AuditLogs
                .Where(log => log.TargetType == "support_session" && log.TargetId == session.Id)
                .ToListAsync();
            var audit = Assert.Single(audits);
            Assert.Equal(caller.Id, audit.ActorId);
            Assert.Equal("support.session.transfer", audit.Action);
        }

        // Kiểm tra hàng chờ view=mine của người nhận có transferNote của lần chuyển.
        var queueReq = ActorHttp.CreateRequest(
            HttpMethod.Get,
            "/api/admin/support/sessions?view=mine",
            $"pk_session={receiverToken}");
        var queueRes = await _client.SendAsync(queueReq);
        Assert.Equal(HttpStatusCode.OK, queueRes.StatusCode);
        var queueBody = await queueRes.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<SupportSessionQueueItemResponse>>>();
        Assert.NotNull(queueBody);
        var mineItem = Assert.Single(queueBody.Data);
        Assert.Equal(session.Id, mineItem.Id);
        Assert.Equal(noteText, mineItem.TransferNote);
    }

    [Fact]
    // Chuyển phiên phát sự kiện session.assigned tới kênh nhân viên.
    public async Task Transfer_Success_EmitsSessionAssignedEvent()
    {
        var (caller, callerToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-sse-caller");
        var (receiver, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-sse-receiver");

        await SupportDeskTestKit.SetPresenceRowAsync(
            database, receiver.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: caller.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        using var listener = _staffHub.Register("test-listener");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(session.Id),
            new { staffId = receiver.Id, note = (string?)null },
            $"pk_session={callerToken}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Đọc sự kiện miền phát qua SupportEventHub.
        var foundAssigned = false;
        while (listener.Reader.TryRead(out var ev))
        {
            if (ev.Name == SupportEventNames.SessionAssigned)
            {
                var payload = (SessionEventData)ev.Data;
                if (payload.SessionId == session.Id)
                {
                    foundAssigned = true;
                    break;
                }
            }
        }

        Assert.True(foundAssigned);
    }

    [Fact]
    // Hai request chuyển đồng thời từ cùng người giữ: người thắng trả 200, người thua trả 403.
    public async Task Transfer_ConcurrentRequestsFromSameHolder_Winner200Loser403()
    {
        var (caller, callerToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-race-caller");
        var (receiver1, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-race-rec1");
        var (receiver2, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "transfer-race-rec2");

        await SupportDeskTestKit.SetPresenceRowAsync(
            database, receiver1.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, receiver2.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: caller.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        var gate = new ParallelTransferReadGate();
        await using var host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(gate)));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var staffHub = host.Services.GetRequiredService<SupportEventHub>();
        using var staffListener = staffHub.Register("staff-transfer-race-listener");

        var req1 = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(session.Id),
            new { staffId = receiver1.Id, note = "Chuyển 1" },
            $"pk_session={callerToken}");
        var req2 = ActorHttp.CreateJsonRequest(
            HttpMethod.Put,
            AssignmentPath(session.Id),
            new { staffId = receiver2.Id, note = "Chuyển 2" },
            $"pk_session={callerToken}");

        var responses = await Task.WhenAll(client.SendAsync(req1), client.SendAsync(req2));

        Assert.True(gate.BothRunsRead);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Forbidden, statusCodes);

        var forbiddenResponse = responses.Single(r => r.StatusCode == HttpStatusCode.Forbidden);
        var doc = JsonDocument.Parse(await forbiddenResponse.Content.ReadAsStringAsync());
        Assert.Equal(SupportDeskErrorCodes.SessionNotAssigned, doc.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra đúng 1 dòng audit log chuyển phiên được ghi nhận.
        await using var identityContext = database.CreateIdentityContext();
        var audits = await identityContext.AuditLogs
            .Where(log => log.TargetType == "support_session" && log.TargetId == session.Id && log.Action == "support.session.transfer")
            .ToListAsync();
        Assert.Single(audits);

        // Kiểm tra đúng 1 sự kiện `session.assigned` được phát tới nhân viên.
        var assignedCount = 0;
        while (staffListener.Reader.TryRead(out var ev))
        {
            if (ev.Name == SupportEventNames.SessionAssigned && ((SessionEventData)ev.Data).SessionId == session.Id)
            {
                assignedCount++;
            }
        }
        Assert.Equal(1, assignedCount);
    }

    // Cổng giữ hai request chuyển cùng đọc phiên trước khi một request ghi.
    private sealed class ParallelTransferReadGate : DbCommandInterceptor
    {
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);
        private readonly HashSet<DbContext> _countedContexts = [];
        private readonly Lock _lock = new();
        private TaskCompletionSource _pairReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readerCount;

        public bool BothRunsRead => Volatile.Read(ref _readerCount) >= 2;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var isSessionRead = command.CommandText.Contains("FROM support_sessions", StringComparison.Ordinal);
            if (!isSessionRead || eventData.Context is null)
            {
                return result;
            }

            if (ReachPair(eventData.Context) is { } released)
            {
                await released.WaitAsync(MaxWait, cancellationToken);
            }

            return result;
        }

        private Task? ReachPair(DbContext context)
        {
            lock (_lock)
            {
                if (!_countedContexts.Add(context))
                {
                    return null;
                }

                _readerCount++;
                if (_readerCount % 2 == 0)
                {
                    var completedPair = _pairReleased;
                    _pairReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    completedPair.TrySetResult();
                    return null;
                }

                return _pairReleased.Task;
            }
        }
    }
}
