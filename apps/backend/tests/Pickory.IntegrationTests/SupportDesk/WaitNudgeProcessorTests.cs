using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Sessions;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-04: job mỗi phút nhắc lại phiên chờ quá `WaitNudgeMinutes`. Test gọi thẳng processor với đồng hồ giả, không chờ timer.
// Mốc nhắc lại đọc từ dòng cấu hình, nên test không viết cứng 10 phút.
[Collection(SupportDeskCollection.Name)]
public sealed class WaitNudgeProcessorTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    // Nội dung tin nhắc lại theo spec. Test khóa nguyên câu, nên đổi câu trong code thì test phải đỏ.
    private const string ReminderBody = "Nhân viên CSKH đang bận. Bạn có thể để lại SĐT để CSKH gọi lại.";

    private readonly FakeTimeProvider _clock = new(SupportSessionTestKit.InHoursMoment);
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        // Bảng trực dùng chung cho cả bộ test, nên mỗi test bắt đầu không có nhân viên nào.
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await SupportSessionTestKit.ResetSettingsAsync(database);
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    [Fact]
    public async Task Process_BeforeTheNudgeThreshold_DoesNotRemind()
    {
        var threshold = await ReadNudgeThresholdAsync();
        var conversation = await SeedWaitingSessionAsync(threshold - TimeSpan.FromMinutes(1));

        var remindedCount = await RunProcessorAsync();

        Assert.Equal(0, remindedCount);
        Assert.Empty(await SupportConversationTestKit.ReadMessagesByRoleAsync(
            database, conversation.Id, SupportMessageRole.System));
        Assert.Null(Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id)).NudgedAt);
    }

    [Fact]
    public async Task Process_ExactlyAtTheNudgeThreshold_Reminds()
    {
        // Mốc nửa mở: chờ đúng `WaitNudgeMinutes` phút đã tới lúc nhắc, chưa tới thì chưa nhắc.
        var threshold = await ReadNudgeThresholdAsync();
        var conversation = await SeedWaitingSessionAsync(threshold);

        var remindedCount = await RunProcessorAsync();

        Assert.Equal(1, remindedCount);
        Assert.Single(await ReadReminderMessagesAsync(conversation.Id));
    }

    [Fact]
    public async Task Process_PastTheThreshold_WritesTheReminderWithCallbackFormAndStampsNudgedAt()
    {
        var threshold = await ReadNudgeThresholdAsync();
        var conversation = await SeedWaitingSessionAsync(threshold + TimeSpan.FromMinutes(2));

        var remindedCount = await RunProcessorAsync();

        Assert.Equal(1, remindedCount);
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        // Mốc nhắc lại ghi đúng một lần, để vòng sau không nhắc lại phiên này.
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, session.NudgedAt);
        var reminder = Assert.Single(await ReadReminderMessagesAsync(conversation.Id));
        Assert.Equal(SupportMessageRole.System, reminder.Role);
        Assert.Equal(ReminderBody, reminder.Body);
        // Trang `/support` mở form số điện thoại từ `action` của tin nhắc lại.
        Assert.Equal("callback_form", reminder.Action);
        // Tin nhắc lại ghi muộn hơn mốc chạy 1 mili giây, giống tin hệ thống của bước mở phiên.
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddMilliseconds(1), reminder.CreatedAt);
        // Tin nhắc lại làm hội thoại mới hơn. Hạn lưu và hạn đóng im lặng tính lại từ mốc này.
        Assert.Equal(
            _clock.GetUtcNow().UtcDateTime,
            (await SupportConversationTestKit.ReadConversationAsync(database, conversation.Id)).LastMessageAt);
    }

    [Fact]
    public async Task Process_RunTwice_RemindsEachSessionOnlyOnce()
    {
        var threshold = await ReadNudgeThresholdAsync();
        var conversation = await SeedWaitingSessionAsync(threshold + TimeSpan.FromMinutes(5));

        var firstRun = await RunProcessorAsync();
        var secondRun = await RunProcessorAsync();

        Assert.Equal(1, firstRun);
        // Lần chạy thứ hai không còn phiên nào cần nhắc, vì `nudgedAt` đã có giá trị.
        Assert.Equal(0, secondRun);
        Assert.Single(await ReadReminderMessagesAsync(conversation.Id));
    }

    [Fact]
    public async Task Process_SessionOpenedOutsideHours_IsNeverReminded()
    {
        // Phiên ngoài giờ không có nhân viên trực, nên nhắc lại không giúp khách và không được gửi.
        var conversation = await SeedWaitingSessionAsync(TimeSpan.FromHours(2), handoffInHours: false);

        var remindedCount = await RunProcessorAsync();

        Assert.Equal(0, remindedCount);
        Assert.Empty(await ReadReminderMessagesAsync(conversation.Id));
        Assert.Null(Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id)).NudgedAt);
    }

    [Fact]
    public async Task Process_ClaimedOrClosedSession_IsNotReminded()
    {
        var threshold = await ReadNudgeThresholdAsync();
        var handoffAt = _clock.GetUtcNow().UtcDateTime - threshold - TimeSpan.FromMinutes(30);
        // Phiên đã có người nhận thì khách đang được hỗ trợ, không phải chờ nên không nhắc lại.
        var claimedConversation = await SeedConversationAsync();
        await SupportSessionTestKit.SeedSessionAsync(
            database,
            claimedConversation,
            handoffAt,
            staffId: "staff-1",
            claimedAt: handoffAt.AddMinutes(1));
        // Phiên đã kết thúc cũng không thuộc hàng chờ.
        var closedConversation = await SeedConversationAsync();
        await SupportSessionTestKit.SeedSessionAsync(
            database,
            closedConversation,
            handoffAt,
            staffId: "staff-1",
            claimedAt: handoffAt.AddMinutes(1),
            closedAt: handoffAt.AddMinutes(5));

        var remindedCount = await RunProcessorAsync();

        Assert.Equal(0, remindedCount);
        Assert.Empty(await ReadReminderMessagesAsync(claimedConversation.Id));
        Assert.Empty(await ReadReminderMessagesAsync(closedConversation.Id));
    }

    [Fact]
    public async Task Process_ShortenedThresholdInSettings_RemindsSooner()
    {
        await SupportSessionTestKit.UpdateSettingsAsync(database, waitNudgeMinutes: 3);
        var conversation = await SeedWaitingSessionAsync(TimeSpan.FromMinutes(4));

        var remindedCount = await RunProcessorAsync();

        // Bốn phút chờ chưa tới mốc mặc định nhưng đã qua mốc 3 phút mà admin đặt.
        Assert.Equal(1, remindedCount);
        Assert.Single(await ReadReminderMessagesAsync(conversation.Id));
    }

    [Fact]
    public async Task Process_TwoOverlappingRuns_RemindTheSessionOnce()
    {
        var threshold = await ReadNudgeThresholdAsync();
        var conversation = await SeedWaitingSessionAsync(threshold + TimeSpan.FromMinutes(5));
        // Cổng giữ cả hai vòng ở bước đọc danh sách, để cả hai cùng thấy phiên cần nhắc.
        var gate = new ParallelNudgeReadGate();
        await using var host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(gate)));
        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();
        var firstProcessor = firstScope.ServiceProvider.GetRequiredService<WaitNudgeProcessor>();
        var secondProcessor = secondScope.ServiceProvider.GetRequiredService<WaitNudgeProcessor>();

        var counts = await Task.WhenAll(
            firstProcessor.RemindWaitingSessionsAsync(CancellationToken.None),
            secondProcessor.RemindWaitingSessionsAsync(CancellationToken.None));

        // Cả hai vòng đã đọc danh sách trước khi vòng nào ghi, nên nhánh tranh chấp thật sự chạy.
        Assert.True(gate.BothRunsRead);
        // Câu UPDATE có điều kiện nên chỉ một vòng nhắc được phiên này.
        Assert.Equal(1, counts.Sum());
        Assert.Single(await ReadReminderMessagesAsync(conversation.Id));
    }

    private async Task<int> RunProcessorAsync()
    {
        // Processor cần scope riêng, giống một vòng của worker chạy nền.
        await using var scope = _host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<WaitNudgeProcessor>();
        return await processor.RemindWaitingSessionsAsync(CancellationToken.None);
    }

    // Mốc nhắc lại đọc từ dòng cấu hình, nên test vẫn đúng khi admin đổi giá trị mặc định.
    private async Task<TimeSpan> ReadNudgeThresholdAsync()
    {
        var settings = await SupportSessionTestKit.ReadSettingsAsync(database);
        return TimeSpan.FromMinutes(settings.WaitNudgeMinutes);
    }

    private async Task<SupportConversation> SeedConversationAsync()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        return await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
    }

    // Gieo phiên chờ đã chờ đúng khoảng thời gian truyền vào, tính từ đồng hồ của host.
    private async Task<SupportConversation> SeedWaitingSessionAsync(TimeSpan waitingFor, bool handoffInHours = true)
    {
        var conversation = await SeedConversationAsync();
        await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime - waitingFor,
            handoffInHours: handoffInHours);
        return conversation;
    }

    // Tin nhắc lại là tin hệ thống duy nhất của phiên, vì test không mở phiên qua route.
    private async Task<IReadOnlyList<SupportMessage>> ReadReminderMessagesAsync(string conversationId) =>
        await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversationId, SupportMessageRole.System);

    // Giữ vòng chạy đầu tiên ở bước đọc danh sách cho tới khi vòng thứ hai cũng đã đọc.
    private sealed class ParallelNudgeReadGate : DbCommandInterceptor
    {
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);

        // Mỗi `DbContext` chỉ được tính một lần, vì một vòng chạy có thể đọc lại bảng phiên.
        private readonly HashSet<DbContext> _countedContexts = [];
        private readonly Lock _lock = new();
        private TaskCompletionSource _pairReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readerCount;

        // Hai vòng chạy đã cùng đọc danh sách, nên chúng chắc chắn chồng nhau ở bước ghi.
        public bool BothRunsRead => Volatile.Read(ref _readerCount) >= 2;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            // Chỉ câu đọc danh sách phiên cần nhắc mới bị giữ. Các câu đọc cấu hình không bị ảnh hưởng.
            var isCandidateRead = command.CommandText.Contains("FROM support_sessions", StringComparison.Ordinal);
            if (!isCandidateRead || eventData.Context is null)
            {
                return result;
            }

            // Vòng đọc trước chờ vòng đọc sau. Hết hạn chờ thì ném lỗi để test đỏ thay vì treo.
            if (ReachPair(eventData.Context) is { } released)
            {
                await released.WaitAsync(MaxWait, cancellationToken);
            }

            return result;
        }

        // Trả task phải chờ, hoặc `null` khi lần đọc này hoàn tất cặp và thả vòng còn lại.
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
