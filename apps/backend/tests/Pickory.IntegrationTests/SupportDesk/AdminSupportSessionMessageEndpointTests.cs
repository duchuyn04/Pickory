using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Sessions;
using Pickory.Shared.Http;
using Xunit;
namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra endpoint POST /api/admin/support/sessions/{id}/messages theo tiêu chí SD-07.
[Collection(SupportDeskCollection.Name)]
public sealed class AdminSupportSessionMessageEndpointTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset InitialMoment = new(2026, 11, 3, 2, 17, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(InitialMoment);
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

    private static string MessagesPath(string sessionId) =>
        $"/api/admin/support/sessions/{sessionId}/messages";

    [Fact]
    // Nhân viên không có quyền conversations:reply phải nhận 403 Forbidden từ middleware.
    public async Task Post_SalesWithoutReplyPermission_Returns403()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SalesCode, "msg-sales");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath("dummy-id"), new { body = "Xin chào" }, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    // Người không giữ phiên gửi tin phải nhận lỗi 403 với mã SUPPORT_SESSION_NOT_ASSIGNED.
    public async Task Post_StaffNotAssignedToSession_Returns403SessionNotAssigned()
    {
        var (staffOwner, _) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "owner-staff");
        var (_, otherStaffToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "other-staff");

        var session = await SeedClaimedSessionAsync(staffOwner.Id);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "Chào bạn." }, $"pk_session={otherStaffToken}");
        var response = await _client.SendAsync(request);

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.Forbidden, "SUPPORT_SESSION_NOT_ASSIGNED");
    }

    [Fact]
    // Gửi body rỗng hoặc dài hơn 2000 ký tự phải trả lỗi 400 VALIDATION_ERROR.
    public async Task Post_InvalidBody_Returns400ValidationError()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "val-staff");
        var session = await SeedClaimedSessionAsync(staff.Id);

        // Kiểm tra body rỗng.
        var emptyRequest = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "   " }, $"pk_session={token}");
        var emptyResponse = await _client.SendAsync(emptyRequest);
        await SupportConversationTestKit.AssertErrorAsync(
            emptyResponse, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        // Kiểm tra body 2001 ký tự.
        var longBody = new string('a', 2001);
        var longRequest = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = longBody }, $"pk_session={token}");
        var longResponse = await _client.SendAsync(longRequest);
        await SupportConversationTestKit.AssertErrorAsync(
            longResponse, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    // Body đúng 2000 ký tự hợp lệ phải trả về 201 Created.
    public async Task Post_BodyOfExactly2000Characters_Returns201()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "len-staff");
        var session = await SeedClaimedSessionAsync(staff.Id);

        var validBody = new string('a', 2000);
        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = validBody }, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    // Gửi tin vào phiên không tồn tại phải nhận 404 SUPPORT_SESSION_NOT_FOUND.
    public async Task Post_UnknownSession_Returns404SessionNotFound()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "404-staff");

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(Guid.NewGuid().ToString()), new { body = "Xin chào" }, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.NotFound, "SUPPORT_SESSION_NOT_FOUND");
    }

    [Fact]
    // Gửi tin vào phiên đã đóng phải nhận 409 SUPPORT_SESSION_CLOSED.
    public async Task Post_ClosedSession_Returns409SessionClosed()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "closed-staff");
        var session = await SeedClaimedSessionAsync(staff.Id, closed: true);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "Xin chào" }, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        await SupportConversationTestKit.AssertErrorAsync(
            response, HttpStatusCode.Conflict, "SUPPORT_SESSION_CLOSED");
    }

    [Fact]
    // Tin nhân viên đầu tiên ghi first_response_at và tin thứ hai không đổi giá trị này.
    public async Task Post_FirstMessageSetsFirstResponseAt_SecondMessagePreservesIt()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "resp-staff");
        var session = await SeedClaimedSessionAsync(staff.Id);

        var firstMoment = _clock.GetUtcNow().UtcDateTime;
        var firstRequest = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "Tin đầu tiên" }, $"pk_session={token}");
        var firstResponse = await _client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Kiểm tra DB đã cập nhật first_response_at đúng mốc tin đầu tiên.
        await using (var context = database.CreateSupportDeskContext())
        {
            var updated = await context.Sessions.SingleAsync(s => s.Id == session.Id);
            Assert.NotNull(updated.FirstResponseAt);
            Assert.Equal(firstMoment, updated.FirstResponseAt.Value);
        }

        // Tiến đồng hồ thêm 5 phút trước khi gửi tin thứ hai.
        _clock.Advance(TimeSpan.FromMinutes(5));
        var secondRequest = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "Tin thứ hai" }, $"pk_session={token}");
        var secondResponse = await _client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        // Mốc first_response_at phải giữ nguyên giá trị cũ của tin đầu tiên.
        await using (var context = database.CreateSupportDeskContext())
        {
            var updated = await context.Sessions.SingleAsync(s => s.Id == session.Id);
            Assert.Equal(firstMoment, updated.FirstResponseAt!.Value);
        }
    }

    [Fact]
    // Tin nhân viên hiển thị tên hồ sơ đọc qua IStaffDirectory.
    public async Task Post_StaffWithProfileName_ReturnsStaffDisplayName()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "name-staff");

        // Cập nhật tên hồ sơ nhân viên trong Identity DB.
        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = "Lan - CSKH";
            await identity.SaveChangesAsync();
        }

        var session = await SeedClaimedSessionAsync(staff.Id);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "Chào bạn" }, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        var data = root!.Data;
        Assert.Equal("Lan - CSKH", data.GetProperty("staffDisplayName").GetString());
        Assert.Equal(session.Id, data.GetProperty("sessionId").GetString());
        Assert.Equal("staff", data.GetProperty("role").GetString());
        Assert.Equal("Chào bạn", data.GetProperty("body").GetString());
    }

    [Fact]
    // Tên hồ sơ chứa ký tự @ hợp lệ vẫn được giữ nguyên làm staffDisplayName.
    public async Task Post_StaffWithAtSignInName_ReturnsStaffDisplayName()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "at-staff");

        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = "Lan @ CSKH";
            await identity.SaveChangesAsync();
        }

        var session = await SeedClaimedSessionAsync(staff.Id);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "Chào bạn" }, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        var data = root!.Data;
        Assert.Equal("Lan @ CSKH", data.GetProperty("staffDisplayName").GetString());
    }

    [Fact]
    // Hồ sơ thiếu tên thì staffDisplayName bằng null và không chứa email nhân viên.
    public async Task Post_StaffWithoutProfileName_ReturnsNullStaffDisplayNameAndNoEmail()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "noname-staff");

        // Xóa tên trong hồ sơ nhân viên để kiểm tra trường hợp chỉ có email.
        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = null;
            await identity.SaveChangesAsync();
        }

        var session = await SeedClaimedSessionAsync(staff.Id);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "Chào bạn" }, $"pk_session={token}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rawJson = await response.Content.ReadAsStringAsync();
        // Phản hồi không được chứa địa chỉ email của nhân viên.
        Assert.DoesNotContain(staff.Email, rawJson, StringComparison.OrdinalIgnoreCase);

        var root = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
        var data = root!.Data;
        Assert.Equal(JsonValueKind.Null, data.GetProperty("staffDisplayName").ValueKind);
    }

    [Fact]
    // Hai request gửi tin song song: mốc first_response_at thuộc tin đầu được ghi và không bị ghi đè.
    public async Task Post_TwoParallelRequests_PreservesFirstResponseAtOfFirstCommittedMessage()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "conc-staff");
        var session = await SeedClaimedSessionAsync(staff.Id);

        var firstMoment = InitialMoment.UtcDateTime;
        var secondMoment = InitialMoment.UtcDateTime.AddMinutes(5);
        var clock = new SteppingTimeProvider(firstMoment, secondMoment);
        var gate = new ConcurrentSessionReadGate();
        var commitNotifier = new NotifyCommitInterceptor(gate);

        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.ConfigureDbContext<SupportDeskDbContext>(options =>
            {
                options.AddInterceptors(gate, commitNotifier);
            });
        }));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var req1 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "Tin nhắn thứ nhất" }, $"pk_session={token}");
        var req2 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post, MessagesPath(session.Id), new { body = "Tin nhắn thứ hai" }, $"pk_session={token}");

        var responses = await Task.WhenAll(client.SendAsync(req1), client.SendAsync(req2));
        Assert.All(responses, res => Assert.Equal(HttpStatusCode.Created, res.StatusCode));
        Assert.True(gate.BothRequestsReadBeforeAnyUpdate);

        await using var context = database.CreateSupportDeskContext();
        var sessionAfter = await context.Sessions.SingleAsync(s => s.Id == session.Id);
        Assert.NotNull(sessionAfter.FirstResponseAt);
        // Mốc first_response_at phải giữ giá trị của tin đầu tiên đã commit.
        Assert.Equal(firstMoment, sessionAfter.FirstResponseAt.Value);
    }

    // Đồng hồ giả trả mốc giờ tăng dần cho từng request gửi tin nhắn.
    private sealed class SteppingTimeProvider(DateTimeOffset first, DateTimeOffset second) : TimeProvider
    {
        private int _serviceCallCount;

        public override DateTimeOffset GetUtcNow()
        {
            var frames = new System.Diagnostics.StackTrace().GetFrames();
            if (frames != null)
            {
                foreach (var frame in frames)
                {
                    var type = frame.GetMethod()?.DeclaringType;
                    if (type != null && (type == typeof(SupportSessionMessageService) || type.DeclaringType == typeof(SupportSessionMessageService) || type.Name.Contains(nameof(SupportSessionMessageService))))
                    {
                        var count = Interlocked.Increment(ref _serviceCallCount);
                        return count == 1 ? first : second;
                    }
                }
            }

            return first;
        }
    }

    // Cổng chặn cả hai request ở bước đọc phiên ban đầu.
    private sealed class ConcurrentSessionReadGate : DbCommandInterceptor
    {
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);
        private readonly HashSet<DbContext> _contextsThatRead = [];
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstCommitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DbContext? _firstContext;

        public bool BothRequestsReadBeforeAnyUpdate => _bothRead.Task.IsCompletedSuccessfully;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            var isSessionRead = command.CommandText.Contains("FROM support_sessions", StringComparison.Ordinal);
            if (isSessionRead && eventData.Context is not null && RegisterFirstRead(eventData.Context))
            {
                if (eventData.Context == _firstContext)
                {
                    // Request thứ nhất chờ request thứ hai cùng đọc xong phiên trước khi tiếp tục.
                    await _bothRead.Task.WaitAsync(MaxWait, cancellationToken);
                }
                else
                {
                    // Request thứ hai chờ request thứ nhất commit xong trước khi tiếp tục.
                    await _firstCommitted.Task.WaitAsync(MaxWait, cancellationToken);
                }
            }

            return result;
        }

        public void NotifyFirstCommitted(DbContext context)
        {
            if (context == _firstContext)
            {
                _firstCommitted.TrySetResult();
            }
        }

        private bool RegisterFirstRead(DbContext context)
        {
            lock (_contextsThatRead)
            {
                if (!_contextsThatRead.Add(context))
                {
                    return false;
                }

                if (_contextsThatRead.Count == 1)
                {
                    _firstContext = context;
                }
                else if (_contextsThatRead.Count == 2)
                {
                    _bothRead.TrySetResult();
                }

                return true;
            }
        }
    }

    private sealed class NotifyCommitInterceptor(ConcurrentSessionReadGate gate) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
            {
                gate.NotifyFirstCommitted(eventData.Context);
            }
            return Task.CompletedTask;
        }
    }

    private async Task<SupportSession> SeedClaimedSessionAsync(string staffId, bool closed = false)
    {
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, Guid.NewGuid().ToString());

        await using var context = database.CreateSupportDeskContext();
        var session = new SupportSession
        {
            ConversationId = conversation.Id,
            HandoffAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
            HandoffInHours = true,
            AnonymousId = conversation.AnonymousId,
            StaffId = staffId,
            ClaimedAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-5),
            ClosedAt = closed ? _clock.GetUtcNow().UtcDateTime : null,
            ClosedBy = closed ? staffId : null,
        };
        context.Sessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }
}
