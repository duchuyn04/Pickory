using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pickory.Modules.SupportDesk.Persistence;
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
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Modules.SupportDesk.Sessions;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra endpoint `PATCH /api/admin/support/sessions/{id}` cho kết thúc phiên (SD-08).
[Collection(SupportDeskCollection.Name)]
public sealed class AdminSupportSessionCloseEndpointTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;
    private SupportEventHub _staffHub = null!;
    private CustomerEventHub _customerHub = null!;

    public async Task InitializeAsync()
    {
        // Xóa bảng trực trước mỗi test để trạng thái kiểm thử độc lập.
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _staffHub = _host.Services.GetRequiredService<SupportEventHub>();
        _customerHub = _host.Services.GetRequiredService<CustomerEventHub>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        // Dọn dẹp trạng thái trực và dữ liệu hội thoại sau khi hoàn tất test.
        await SupportDeskTestKit.ClearPresenceAsync(database);
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    private static string ClosePath(string sessionId) =>
        $"/api/admin/support/sessions/{sessionId}";

    [Fact]
    // Nhân viên bán hàng không có quyền conversations:reply phải nhận 403 Forbidden.
    public async Task Close_WithoutPermission_Returns403()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SalesCode, "close-sales");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch,
            ClosePath("dummy-id"),
            new { status = "closed" },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("pending")]
    [InlineData("")]
    // Trạng thái khác closed phải trả lỗi 400 VALIDATION_ERROR.
    public async Task Close_StatusNotClosed_Returns400(string invalidStatus)
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-invalid-status");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch,
            ClosePath("dummy-id"),
            new { status = invalidStatus },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Phiên không tồn tại phải trả 404 SUPPORT_SESSION_NOT_FOUND.
    public async Task Close_SessionNotFound_Returns404()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-not-found");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch,
            ClosePath(Guid.NewGuid().ToString()),
            new { status = "closed" },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SupportDeskErrorCodes.SessionNotFound, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Phiên đã đóng phải trả 409 SUPPORT_SESSION_CLOSED.
    public async Task Close_SessionAlreadyClosed_Returns409()
    {
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-already-closed");
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
            HttpMethod.Patch,
            ClosePath(session.Id),
            new { status = "closed" },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SupportDeskErrorCodes.SessionClosed, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Người không giữ phiên kết thúc phải nhận 403 `SUPPORT_SESSION_NOT_ASSIGNED`.
    public async Task Close_NotAssignedToCaller_Returns403()
    {
        var (otherStaff, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-owner");
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-caller");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: otherStaff.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch,
            ClosePath(session.Id),
            new { status = "closed" },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SupportDeskErrorCodes.SessionNotAssigned, document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Kết thúc phiên thành công cập nhật phiên, gửi tin CSAT và ghi đúng 1 audit log.
    public async Task Close_Success_UpdatesSessionAndSendsCsatMessageAndWritesAudit()
    {
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-success");
        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: caller.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        var closeMoment = _clock.GetUtcNow().UtcDateTime;

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch,
            ClosePath(session.Id),
            new { status = "closed" },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SupportSessionCloseResponse>>();
        Assert.NotNull(body);
        Assert.Equal(session.Id, body.Data.SessionId);
        Assert.Equal(closeMoment, body.Data.ClosedAt);
        Assert.Equal(caller.Id, body.Data.ClosedBy);

        // Kiểm tra cơ sở dữ liệu đã cập nhật trạng thái đóng phiên.
        await using (var context = database.CreateSupportDeskContext())
        {
            var updatedSession = await context.Sessions.SingleAsync(s => s.Id == session.Id);
            Assert.Equal(closeMoment, updatedSession.ClosedAt);
            Assert.Equal(caller.Id, updatedSession.ClosedBy);

            // Kiểm tra tin nhắn hệ thống mời chấm điểm CSAT đã tạo trong hội thoại.
            var csatMessage = await context.Messages
                .SingleOrDefaultAsync(m => m.SessionId == session.Id && m.Role == SupportMessageRole.System && m.Action == "csat_form");
            Assert.NotNull(csatMessage);
            Assert.Equal(SupportSystemMessages.SessionClosed, csatMessage.Body);
            Assert.Equal(closeMoment, csatMessage.CreatedAt);
        }

        // Kiểm tra đúng 1 dòng nhật ký kiểm toán support.session.close được ghi.
        await using (var identityContext = database.CreateIdentityContext())
        {
            var audits = await identityContext.AuditLogs
                .Where(log => log.TargetType == "support_session" && log.TargetId == session.Id)
                .ToListAsync();
            var audit = Assert.Single(audits);
            Assert.Equal(caller.Id, audit.ActorId);
            Assert.Equal("support.session.close", audit.Action);
        }
    }

    [Fact]
    // Kết thúc phiên phát sự kiện session.closed tới cả khách và nhân viên.
    public async Task Close_EmitsEventsToCustomerAndStaff()
    {
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-events");
        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: caller.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        using var staffListener = _staffHub.Register("staff-close-listener");
        using var customerListener = _customerHub.Register(conversation.Id);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch,
            ClosePath(session.Id),
            new { status = "closed" },
            $"pk_session={token}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Kiểm tra sự kiện session.closed tới nhân viên qua SupportEventHub.
        var foundStaffClosed = false;
        while (staffListener.Reader.TryRead(out var ev))
        {
            if (ev.Name == SupportEventNames.SessionClosed)
            {
                var payload = (SessionEventData)ev.Data;
                if (payload.SessionId == session.Id)
                {
                    foundStaffClosed = true;
                    break;
                }
            }
        }
        Assert.True(foundStaffClosed);

        // Kiểm tra sự kiện session.closed và message.created (csat) tới khách qua CustomerEventHub.
        var foundCustomerClosed = false;
        var foundCsatMessage = false;
        while (customerListener.Reader.TryRead(out var item))
        {
            if (item is CustomerStreamItem.Event ev)
            {
                if (ev.Name == CustomerEventNames.SessionClosed)
                {
                    var payload = (CustomerSessionEventData)ev.Data;
                    if (payload.SessionId == session.Id)
                    {
                        foundCustomerClosed = true;
                    }
                }
                else if (ev.Name == CustomerEventNames.MessageCreated)
                {
                    var payload = (CustomerMessageCreatedEventData)ev.Data;
                    if (payload.Action == "csat_form")
                    {
                        foundCsatMessage = true;
                    }
                }
            }
        }
        Assert.True(foundCustomerClosed);
        Assert.True(foundCsatMessage);
    }

    [Fact]
    // Sau khi phiên kết thúc, tin mới của khách mở phiên chờ mới trong cùng hội thoại.
    public async Task Close_NewCustomerMessage_OpensNewWaitingSessionInSameConversation()
    {
        var (caller, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-reopen");
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var anonymousId = SupportConversationTestKit.ReadAnonymousId(guestCookie);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, anonymousId);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            handoffInHours: true,
            staffId: caller.Id,
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        // Đóng phiên qua route PATCH.
        var closeRequest = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch,
            ClosePath(session.Id),
            new { status = "closed" },
            $"pk_session={token}");
        var closeResponse = await _client.SendAsync(closeRequest);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        // Khách gửi tin nhắn mới vào hội thoại sau khi phiên đã đóng.
        var customerSendReq = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/support/conversations/{conversation.Id}/messages",
            new { body = "Tôi cần hỗ trợ thêm vấn đề khác." },
            guestCookie);
        var customerSendRes = await _client.SendAsync(customerSendReq);
        Assert.Equal(HttpStatusCode.Created, customerSendRes.StatusCode);

        // Kiểm tra hội thoại hiện có đúng một phiên mở mới và phiên cũ đã đóng.
        await using var context = database.CreateSupportDeskContext();
        var sessions = await context.Sessions
            .Where(s => s.ConversationId == conversation.Id)
            .OrderBy(s => s.HandoffAt)
            .ToListAsync();
        Assert.Equal(2, sessions.Count);

        var oldSession = sessions[0];
        Assert.Equal(session.Id, oldSession.Id);
        Assert.NotNull(oldSession.ClosedAt);

        var newSession = sessions[1];
        Assert.NotEqual(session.Id, newSession.Id);
        Assert.Null(newSession.ClosedAt);
        Assert.Null(newSession.StaffId);
    }

    [Fact]
    // Tranh chấp giữa kết thúc và chuyển: request kết thúc thua phải trả 403 `SUPPORT_SESSION_NOT_ASSIGNED`.
    public async Task Close_RaceWithTransfer_CloseLosesWith403()
    {
        var (caller, callerToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-race-caller");
        var (receiver, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "close-race-receiver");

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

        HttpClient? clientRef = null;
        var transferExecuted = false;

        var interceptor = new HookBeforeCloseUpdateInterceptor(async () =>
        {
            var transferReq = ActorHttp.CreateJsonRequest(
                HttpMethod.Put,
                $"/api/admin/support/sessions/{session.Id}/assignment",
                new { staffId = receiver.Id, note = "Chuyển trước khi đóng" },
                $"pk_session={callerToken}");
            var transferRes = await clientRef!.SendAsync(transferReq);
            Assert.Equal(HttpStatusCode.OK, transferRes.StatusCode);
            transferExecuted = true;
        });

        await using var host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(interceptor)));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        clientRef = client;
        var staffHub = host.Services.GetRequiredService<SupportEventHub>();
        var customerHub = host.Services.GetRequiredService<CustomerEventHub>();
        using var staffListener = staffHub.Register("staff-race-close-listener");
        using var customerListener = customerHub.Register(conversation.Id);

        var closeReq = ActorHttp.CreateJsonRequest(
            HttpMethod.Patch,
            ClosePath(session.Id),
            new { status = "closed" },
            $"pk_session={callerToken}");

        var closeRes = await client.SendAsync(closeReq);

        Assert.True(transferExecuted);
        // Kết thúc phiên thua cuộc vì không còn giữ phiên phải trả 403 Forbidden.
        Assert.Equal(HttpStatusCode.Forbidden, closeRes.StatusCode);
        var doc = JsonDocument.Parse(await closeRes.Content.ReadAsStringAsync());
        Assert.Equal(SupportDeskErrorCodes.SessionNotAssigned, doc.RootElement.GetProperty("error").GetProperty("code").GetString());

        // Kiểm tra không có tin nhắn CSAT và phiên vẫn chưa đóng.
        await using var verifyContext = database.CreateSupportDeskContext();
        var reloadedSession = await verifyContext.Sessions.SingleAsync(s => s.Id == session.Id);
        Assert.Null(reloadedSession.ClosedAt);
        Assert.Equal(receiver.Id, reloadedSession.StaffId);

        var csatMessages = await verifyContext.Messages
            .Where(m => m.SessionId == session.Id && m.Role == SupportMessageRole.System && m.Action == "csat_form")
            .ToListAsync();
        Assert.Empty(csatMessages);

        // Kiểm tra không có audit log đóng phiên và có đúng 1 audit log chuyển phiên.
        await using var identityContext = database.CreateIdentityContext();
        var closeAudits = await identityContext.AuditLogs
            .Where(log => log.TargetType == "support_session" && log.TargetId == session.Id && log.Action == "support.session.close")
            .ToListAsync();
        Assert.Empty(closeAudits);

        var transferAudits = await identityContext.AuditLogs
            .Where(log => log.TargetType == "support_session" && log.TargetId == session.Id && log.Action == "support.session.transfer")
            .ToListAsync();
        Assert.Single(transferAudits);

        // Kiểm tra không phát sự kiện `session.closed` tới nhân viên khi kết thúc thất bại.
        while (staffListener.Reader.TryRead(out var ev))
        {
            if (ev.Name == SupportEventNames.SessionClosed && ((SessionEventData)ev.Data).SessionId == session.Id)
            {
                Assert.Fail("Không được phát session.closed tới nhân viên khi kết thúc thất bại.");
            }
        }

        // Kiểm tra không phát sự kiện `session.closed` tới khách khi kết thúc thất bại.
        while (customerListener.Reader.TryRead(out var item))
        {
            if (item is CustomerStreamItem.Event ev && ev.Name == CustomerEventNames.SessionClosed && ((CustomerSessionEventData)ev.Data).SessionId == session.Id)
            {
                Assert.Fail("Không được phát session.closed tới khách khi kết thúc thất bại.");
            }
        }
    }

    // Chèn request chuyển phiên ngay trước câu UPDATE kết thúc để mô phỏng tranh chấp.
    private sealed class HookBeforeCloseUpdateInterceptor(Func<Task> onBeforeUpdate) : DbCommandInterceptor
    {
        private int _executed;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE support_sessions", StringComparison.Ordinal)
                && command.CommandText.Contains("closedBy", StringComparison.Ordinal))
            {
                if (Interlocked.Exchange(ref _executed, 1) == 0)
                {
                    await onBeforeUpdate();
                }
            }

            return result;
        }
    }
}
