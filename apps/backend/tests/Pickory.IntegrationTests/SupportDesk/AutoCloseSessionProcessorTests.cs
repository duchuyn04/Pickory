using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Modules.SupportDesk.Sessions;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra bộ xử lý tự đóng phiên im lặng và phiên chờ ngoài giờ theo SD-08.
[Collection(SupportDeskCollection.Name)]
public sealed class AutoCloseSessionProcessorTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new(SupportSessionTestKit.InHoursMoment);
    private WebApplicationFactory<Program> _host = null!;
    private SupportEventHub _staffHub = null!;
    private CustomerEventHub _customerHub = null!;

    public async Task InitializeAsync()
    {
        // Xóa bảng trực trước mỗi test để đảm bảo môi trường độc lập.
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _staffHub = _host.Services.GetRequiredService<SupportEventHub>();
        _customerHub = _host.Services.GetRequiredService<CustomerEventHub>();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        // Dọn dẹp trạng thái cấu hình và dữ liệu hội thoại sau khi hoàn tất test.
        await SupportSessionTestKit.ResetSettingsAsync(database);
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    [Fact]
    // Phiên đã nhận nhưng chưa tới ngưỡng 30 phút im lặng thì không bị đóng.
    public async Task Process_ClaimedSessionBeforeThreshold_DoesNotClose()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedClaimedSessionAsync(threshold - TimeSpan.FromMinutes(1));

        var closedCount = await RunProcessorAsync();

        Assert.Equal(0, closedCount);
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Null(session.ClosedAt);
    }

    [Fact]
    // Phiên đã nhận đúng mốc 30 phút im lặng phải được tự động đóng.
    public async Task Process_ClaimedSessionExactlyAtThreshold_Closes()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedClaimedSessionAsync(threshold);

        var closedCount = await RunProcessorAsync();

        Assert.Equal(1, closedCount);
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, session.ClosedAt);
        Assert.Equal("auto", session.ClosedBy);
    }

    [Fact]
    // Phiên im lặng quá hạn tự đóng với `closedBy = auto`, thêm tin CSAT và không ghi audit.
    public async Task Process_ClaimedSessionPastThreshold_ClosesWithoutAudit()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedClaimedSessionAsync(threshold + TimeSpan.FromMinutes(5));

        var closedCount = await RunProcessorAsync();

        Assert.Equal(1, closedCount);
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, session.ClosedAt);
        Assert.Equal("auto", session.ClosedBy);

        // Kiểm tra tin nhắn hệ thống mời chấm điểm CSAT đã tạo trong hội thoại.
        await using (var context = database.CreateSupportDeskContext())
        {
            var csatMessage = await context.Messages
                .SingleOrDefaultAsync(m => m.SessionId == session.Id && m.Role == SupportMessageRole.System && m.Action == "csat_form");
            Assert.NotNull(csatMessage);
            Assert.Equal(SupportSystemMessages.SessionClosed, csatMessage.Body);
            Assert.Equal(_clock.GetUtcNow().UtcDateTime, csatMessage.CreatedAt);
        }

        // Tự đóng không được ghi bất kỳ dòng audit log nào vào module identity.
        await using (var identityContext = database.CreateIdentityContext())
        {
            var audits = await identityContext.AuditLogs
                .Where(log => log.TargetType == "support_session" && log.TargetId == session.Id)
                .ToListAsync();
            Assert.Empty(audits);
        }
    }

    [Fact]
    // Phiên đã có tin nhắn mới trong vòng 30 phút không bị xem là im lặng.
    public async Task Process_ClaimedSessionWithRecentMessage_DoesNotClose()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedClaimedSessionAsync(threshold + TimeSpan.FromMinutes(10));
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));

        // Thêm tin nhắn mới cách đây 5 phút (nằm trong ngưỡng an toàn).
        await using (var context = database.CreateSupportDeskContext())
        {
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                SessionId = session.Id,
                Role = SupportMessageRole.Customer,
                Body = "Khách vừa gửi thêm tin nhắn phản hồi.",
                CreatedAt = _clock.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(5),
            });
            await context.SaveChangesAsync();
        }

        var closedCount = await RunProcessorAsync();

        Assert.Equal(0, closedCount);
        var reloadedSession = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Null(reloadedSession.ClosedAt);
    }

    [Fact]
    // Phiên chờ mở ngoài giờ chưa ai nhận quá hạn `autoCloseMinutes` phải tự đóng.
    public async Task Process_WaitingSessionOutsideHoursPastThreshold_Closes()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedWaitingSessionAsync(threshold + TimeSpan.FromMinutes(2), handoffInHours: false);

        var closedCount = await RunProcessorAsync();

        Assert.Equal(1, closedCount);
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, session.ClosedAt);
        Assert.Equal("auto", session.ClosedBy);
    }

    [Fact]
    // Phiên chờ mở trong giờ không tự đóng khi chưa ai nhận.
    public async Task Process_WaitingSessionInsideHoursPastThreshold_DoesNotClose()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedWaitingSessionAsync(threshold + TimeSpan.FromMinutes(15), handoffInHours: true);

        var closedCount = await RunProcessorAsync();

        Assert.Equal(0, closedCount);
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Null(session.ClosedAt);
    }

    [Fact]
    // Phiên đã đóng không bị xử lý lại trong các lần chạy sau.
    public async Task Process_AlreadyClosedSession_DoesNotReClose()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedClaimedSessionAsync(threshold + TimeSpan.FromMinutes(5));

        var firstRun = await RunProcessorAsync();
        var secondRun = await RunProcessorAsync();

        Assert.Equal(1, firstRun);
        Assert.Equal(0, secondRun);
    }

    [Fact]
    // Tự đóng phát sự kiện session.closed tới khách và nhân viên.
    public async Task Process_EmitsClosedEventsToStaffAndCustomer()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedClaimedSessionAsync(threshold + TimeSpan.FromMinutes(5));
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));

        using var staffListener = _staffHub.Register("staff-auto-close-listener");
        using var customerListener = _customerHub.Register(conversation.Id);

        var closedCount = await RunProcessorAsync();
        Assert.Equal(1, closedCount);

        // Kiểm tra sự kiện session.closed tới nhân viên.
        var foundStaffClosed = false;
        while (staffListener.Reader.TryRead(out var ev))
        {
            if (ev.Name == SupportEventNames.SessionClosed && ((SessionEventData)ev.Data).SessionId == session.Id)
            {
                foundStaffClosed = true;
                break;
            }
        }
        Assert.True(foundStaffClosed);

        // Kiểm tra sự kiện session.closed và message.created (csat) tới khách.
        var foundCustomerClosed = false;
        var foundCsatMessage = false;
        while (customerListener.Reader.TryRead(out var item))
        {
            if (item is CustomerStreamItem.Event ev)
            {
                if (ev.Name == CustomerEventNames.SessionClosed && ((CustomerSessionEventData)ev.Data).SessionId == session.Id)
                {
                    foundCustomerClosed = true;
                }
                else if (ev.Name == CustomerEventNames.MessageCreated && ((CustomerMessageCreatedEventData)ev.Data).Action == "csat_form")
                {
                    foundCsatMessage = true;
                }
            }
        }
        Assert.True(foundCustomerClosed);
        Assert.True(foundCsatMessage);
    }

    [Fact]
    // Hai vòng chạy đồng thời chỉ đúng 1 vòng đóng được phiên.
    public async Task Process_ConcurrentRuns_OnlyOneRunClosesSession()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedClaimedSessionAsync(threshold + TimeSpan.FromMinutes(5));

        var gate = new ParallelAutoCloseReadGate();
        await using var host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(gate)));
        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();
        var firstProcessor = firstScope.ServiceProvider.GetRequiredService<AutoCloseSessionProcessor>();
        var secondProcessor = secondScope.ServiceProvider.GetRequiredService<AutoCloseSessionProcessor>();

        var counts = await Task.WhenAll(
            firstProcessor.CloseInactiveSessionsAsync(CancellationToken.None),
            secondProcessor.CloseInactiveSessionsAsync(CancellationToken.None));

        // Cổng đảm bảo cả hai vòng chạy đều đã đọc danh sách trước khi thực hiện ghi.
        Assert.True(gate.BothRunsRead);
        // Câu UPDATE có điều kiện đảm bảo chỉ đúng 1 vòng đóng được phiên.
        Assert.Equal(1, counts.Sum());

        await using var verifyContext = database.CreateSupportDeskContext();
        var session = await verifyContext.Sessions.SingleAsync(s => s.ConversationId == conversation.Id);
        Assert.NotNull(session.ClosedAt);
        Assert.Equal("auto", session.ClosedBy);
        // Chỉ đúng 1 tin nhắn hệ thống CSAT được tạo trong hội thoại.
        var csatMessages = await verifyContext.Messages
            .Where(m => m.SessionId == session.Id && m.Role == SupportMessageRole.System && m.Action == "csat_form")
            .ToListAsync();
        Assert.Single(csatMessages);
    }

    private async Task<int> RunProcessorAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<AutoCloseSessionProcessor>();
        return await processor.CloseInactiveSessionsAsync(CancellationToken.None);
    }

    private async Task<TimeSpan> ReadAutoCloseThresholdAsync()
    {
        var settings = await SupportSessionTestKit.ReadSettingsAsync(database);
        return TimeSpan.FromMinutes(settings.AutoCloseMinutes);
    }

    [Fact]
    // Tin mới xuất hiện trước bước UPDATE làm phiên không còn im lặng và không bị đóng.
    public async Task Process_NewMessageBeforeUpdate_DoesNotCloseSession()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedClaimedSessionAsync(threshold + TimeSpan.FromMinutes(5));
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));

        var interceptor = new HookBeforeUpdateInterceptor(async () =>
        {
            await using var context = database.CreateSupportDeskContext();
            context.Messages.Add(new SupportMessage
            {
                ConversationId = conversation.Id,
                SessionId = session.Id,
                Role = SupportMessageRole.Customer,
                Body = "Khách vừa nhắn tin mới trước khi UPDATE chạy.",
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
            });
            await context.SaveChangesAsync();
        });
        await using var host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(interceptor)));
        var staffHub = host.Services.GetRequiredService<SupportEventHub>();
        var customerHub = host.Services.GetRequiredService<CustomerEventHub>();
        using var staffListener = staffHub.Register("staff-race-new-msg");
        using var customerListener = customerHub.Register(conversation.Id);

        await using var scope = host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<AutoCloseSessionProcessor>();

        var closedCount = await processor.CloseInactiveSessionsAsync(CancellationToken.None);

        Assert.True(interceptor.Triggered);
        Assert.Equal(0, closedCount);

        await using var verifyContext = database.CreateSupportDeskContext();
        var reloadedSession = await verifyContext.Sessions.SingleAsync(s => s.Id == session.Id);
        Assert.Null(reloadedSession.ClosedAt);

        var csatMessages = await verifyContext.Messages
            .Where(m => m.SessionId == session.Id && m.Role == SupportMessageRole.System && m.Action == "csat_form")
            .ToListAsync();
        Assert.Empty(csatMessages);

        // Kiểm tra không phát sự kiện `session.closed` tới nhân viên khi có tin mới.
        while (staffListener.Reader.TryRead(out var ev))
        {
            if (ev.Name == SupportEventNames.SessionClosed && ((SessionEventData)ev.Data).SessionId == session.Id)
            {
                Assert.Fail("Không được phát session.closed tới nhân viên khi phiên có tin mới.");
            }
        }

        // Kiểm tra không phát sự kiện `session.closed` tới khách khi có tin mới.
        while (customerListener.Reader.TryRead(out var item))
        {
            if (item is CustomerStreamItem.Event ev && ev.Name == CustomerEventNames.SessionClosed && ((CustomerSessionEventData)ev.Data).SessionId == session.Id)
            {
                Assert.Fail("Không được phát session.closed tới khách khi phiên có tin mới.");
            }
        }
    }

    [Fact]
    // Phiên chờ ngoài giờ được nhận trước bước UPDATE thì không bị tự đóng.
    public async Task Process_SessionClaimedBeforeUpdate_DoesNotCloseWaitingSession()
    {
        var threshold = await ReadAutoCloseThresholdAsync();
        var conversation = await SeedWaitingSessionAsync(threshold + TimeSpan.FromMinutes(5), handoffInHours: false);
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));

        var interceptor = new HookBeforeUpdateInterceptor(async () =>
        {
            await using var context = database.CreateSupportDeskContext();
            await context.Sessions
                .Where(s => s.Id == session.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.StaffId, "staff-claimed-in-race")
                    .SetProperty(s => s.ClaimedAt, _clock.GetUtcNow().UtcDateTime));
        });

        await using var host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(interceptor)));
        var staffHub = host.Services.GetRequiredService<SupportEventHub>();
        var customerHub = host.Services.GetRequiredService<CustomerEventHub>();
        using var staffListener = staffHub.Register("staff-race-claimed");
        using var customerListener = customerHub.Register(conversation.Id);

        await using var scope = host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<AutoCloseSessionProcessor>();

        var closedCount = await processor.CloseInactiveSessionsAsync(CancellationToken.None);

        Assert.True(interceptor.Triggered);
        Assert.Equal(0, closedCount);

        await using var verifyContext = database.CreateSupportDeskContext();
        var reloadedSession = await verifyContext.Sessions.SingleAsync(s => s.Id == session.Id);
        Assert.Null(reloadedSession.ClosedAt);
        Assert.Equal("staff-claimed-in-race", reloadedSession.StaffId);

        var csatMessages = await verifyContext.Messages
            .Where(m => m.SessionId == session.Id && m.Role == SupportMessageRole.System && m.Action == "csat_form")
            .ToListAsync();
        Assert.Empty(csatMessages);

        // Kiểm tra không phát sự kiện `session.closed` tới nhân viên khi phiên đã được nhận.
        while (staffListener.Reader.TryRead(out var ev))
        {
            if (ev.Name == SupportEventNames.SessionClosed && ((SessionEventData)ev.Data).SessionId == session.Id)
            {
                Assert.Fail("Không được phát session.closed tới nhân viên khi phiên đã được nhận.");
            }
        }

        // Kiểm tra không phát sự kiện `session.closed` tới khách khi phiên đã được nhận.
        while (customerListener.Reader.TryRead(out var item))
        {
            if (item is CustomerStreamItem.Event ev && ev.Name == CustomerEventNames.SessionClosed && ((CustomerSessionEventData)ev.Data).SessionId == session.Id)
            {
                Assert.Fail("Không được phát session.closed tới khách khi phiên đã được nhận.");
            }
        }
    }

    private async Task<SupportConversation> SeedClaimedSessionAsync(TimeSpan inactiveFor)
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        var handoffMoment = _clock.GetUtcNow().UtcDateTime - inactiveFor;
        await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            handoffMoment,
            handoffInHours: true,
            staffId: "staff-auto-close-tester",
            claimedAt: handoffMoment);
        return conversation;
    }

    private async Task<SupportConversation> SeedWaitingSessionAsync(TimeSpan waitingFor, bool handoffInHours)
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime - waitingFor,
            handoffInHours: handoffInHours,
            staffId: null,
            claimedAt: null);
        return conversation;
    }

    // Giữ vòng chạy đầu tiên ở bước đọc danh sách cho tới khi vòng thứ hai cũng đã đọc.
    private sealed class ParallelAutoCloseReadGate : DbCommandInterceptor
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
            var isCandidateRead = command.CommandText.Contains("FROM support_sessions", StringComparison.Ordinal);
            if (!isCandidateRead || eventData.Context is null)
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

    // Chèn hành động ngay trước câu lệnh UPDATE để mô phỏng dữ liệu thay đổi sau bước đọc.
    private sealed class HookBeforeUpdateInterceptor(Func<Task> onBeforeUpdate) : DbCommandInterceptor
    {
        private int _executed;

        public bool Triggered => Volatile.Read(ref _executed) > 0;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            // Chỉ bắt câu lệnh UPDATE trên bảng phiên hỗ trợ.
            if (command.CommandText.Contains("UPDATE support_sessions", StringComparison.Ordinal))
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
