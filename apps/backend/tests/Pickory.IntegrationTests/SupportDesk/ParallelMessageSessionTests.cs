using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Realtime;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-04: hai tin của khách gửi cùng lúc chỉ mở một phiên.
// Cổng đọc giữ cả hai request ở bước kiểm phiên. Cả hai chắc chắn cùng đi vào bước chèn phiên.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ParallelMessageSessionTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string StaffEventsPath = "/api/admin/support/events";

    // Số lần lặp. Mỗi lần là một hội thoại mới, để một lần hỏng không che lần khác.
    private const int RoundCount = 3;

    private readonly FakeTimeProvider _clock = new(SupportSessionTestKit.InHoursMoment);
    // Cổng đọc dùng chung cho mọi request của test. Mỗi cặp request được thả sau khi cả hai đã đọc phiên.
    private readonly ParallelSessionReadGate _gate = new();
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;
    private SupportEventHub _staffHub = null!;

    public async Task InitializeAsync()
    {
        await SupportDeskTestKit.ClearPresenceAsync(database);
        // Cổng gắn vào `DbContext` thật của module, không đăng ký lại `DbContext`, vì enum sẽ bị ánh xạ hai lần.
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(_gate)));
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _staffHub = _host.Services.GetRequiredService<SupportEventHub>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportDeskTestKit.ClearPresenceAsync(database);
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    [Fact]
    public async Task Send_TwoParallelFirstMessages_CreateOneSessionAndStoreBothMessagesInEveryRound()
    {
        var (staffUser, staffToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "parallel-staff");
        // `CreateStaffSessionAsync` trả token thô, nên phải ghép tên cookie như trình duyệt gửi.
        var staffCookie = $"pk_session={staffToken}";
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, staffUser.Id, StaffPresenceStatus.Available, _clock.GetUtcNow());
        using var staffStream = await SseTestStream.OpenAsync(_client, StaffEventsPath, staffCookie);
        // Nhân viên sẵn sàng mở được kênh, nên mọi sự kiện phiên chờ dưới đây có kết nối để tới.
        Assert.Equal(HttpStatusCode.OK, staffStream.Response.StatusCode);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_staffHub, 1);

        for (var round = 1; round <= RoundCount; round++)
        {
            var cookie = SupportConversationTestKit.NewGuestCookie();
            var conversation = await SupportConversationTestKit.SeedConversationAsync(
                database, SupportConversationTestKit.ReadAnonymousId(cookie));

            var responses = await Task.WhenAll(
                SendAsync(cookie, conversation, $"Tin A của lượt {round}"),
                SendAsync(cookie, conversation, $"Tin B của lượt {round}"));

            // Cả hai request đều thấy tin của mình được lưu, kể cả request thua ở bước chèn phiên.
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            Assert.Equal(
                2,
                (await SupportConversationTestKit.ReadMessagesByRoleAsync(
                    database, conversation.Id, SupportMessageRole.Customer)).Count);
            // Một phiên, nên cũng chỉ một tin hệ thống báo khách đang chờ.
            Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
            Assert.Single(
                await SupportConversationTestKit.ReadMessagesByRoleAsync(
                    database, conversation.Id, SupportMessageRole.System));
        }

        // Cổng đã buộc cả hai request của mỗi lượt cùng đọc trước khi request nào chèn phiên.
        Assert.Equal(RoundCount, _gate.CompletedPairs);
        _staffHub.Broadcast(new SupportEvent("test.marker", new { }));
        var waitingEvents = await ReadEventsUntilMarkerAsync(staffStream);
        // Mỗi lượt chỉ một phiên nên nhân viên nhận đúng một sự kiện phiên chờ cho mỗi lượt.
        Assert.Equal(RoundCount, waitingEvents.Count);
    }

    // Đọc luồng tới dòng đánh dấu và đếm sự kiện phiên chờ. Không chờ cố định, nên test không phụ thuộc tốc độ máy.
    private static async Task<List<ReceivedSseEvent>> ReadEventsUntilMarkerAsync(SseTestStream staffStream)
    {
        var waitingEvents = new List<ReceivedSseEvent>();
        while (true)
        {
            var received = await staffStream.ReadEventAsync();
            if (received.Name == "test.marker")
            {
                return waitingEvents;
            }

            if (received.Name == SupportEventNames.SessionWaiting)
            {
                waitingEvents.Add(received);
            }
        }
    }

    private Task<HttpResponseMessage> SendAsync(string cookie, SupportConversation conversation, string body) =>
        _client.SendAsync(SupportConversationTestKit.Post(
            SupportConversationTestKit.MessagesPath(conversation.Id), cookie, new { body }));

    // Giữ request ở lần đọc phiên đầu tiên cho tới khi request còn lại của cặp cũng đã đọc.
    // Nhờ vậy cả hai request cùng thấy chưa có phiên. Cả hai đi vào bước chèn, đúng nhánh tranh chấp cần kiểm.
    private sealed class ParallelSessionReadGate : DbCommandInterceptor
    {
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);

        // Mỗi lần đọc đầu tiên của một request được tính một lần. Request đọc lại phiên ở bước sau không bị giữ.
        private readonly HashSet<DbContext> _countedContexts = [];
        private readonly Lock _lock = new();
        private TaskCompletionSource _pairReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _countedReaders;

        // Số cặp request đã cùng đọc xong. Test đọc số này để chứng minh nhánh tranh chấp đã chạy.
        public int CompletedPairs => Volatile.Read(ref _completedPairs);
        private int _completedPairs;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            // Chỉ câu đọc phiên mới là bước kiểm "hội thoại đã có phiên mở chưa".
            var isSessionRead = command.CommandText.Contains("FROM support_sessions", StringComparison.Ordinal);
            if (!isSessionRead || eventData.Context is null)
            {
                return result;
            }

            // Request lẻ chờ request chẵn của cùng cặp. Hết hạn chờ thì ném lỗi để test đỏ thay vì treo.
            if (ReachPair(eventData.Context) is { } released)
            {
                await released.WaitAsync(MaxWait, cancellationToken);
            }

            return result;
        }

        // Trả task phải chờ, hoặc `null` khi lần đọc này hoàn tất cặp và thả request còn lại.
        private Task? ReachPair(DbContext context)
        {
            lock (_lock)
            {
                // Request đọc phiên lần thứ hai không thuộc cặp mới, nên không bị giữ.
                if (!_countedContexts.Add(context))
                {
                    return null;
                }

                _countedReaders++;
                if (_countedReaders % 2 == 0)
                {
                    var completedPair = _pairReleased;
                    // Cặp kế tiếp nhận task mới, nên lượt sau vẫn phải chờ đủ hai request.
                    _pairReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _completedPairs++;
                    completedPair.TrySetResult();
                    return null;
                }

                return _pairReleased.Task;
            }
        }
    }
}
