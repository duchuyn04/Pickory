using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-05: giới hạn 3 yêu cầu gọi lại trong cùng ngày giờ Việt Nam của một `anonymousId`.
// Test dùng đồng hồ giả và rào chắn lệnh đọc, nên không phụ thuộc giờ chạy và không rơi vào tranh chấp ngẫu nhiên.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CallbackRequestDailyLimitTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string CallbackRequestsPath = CallbackRequestTestKit.CallbackRequestsPath;

    // 23:59 ngày 2026-11-03 theo giờ Việt Nam. Mốc cuối ngày để kiểm ranh giới nửa đêm.
    private static readonly DateTimeOffset DayEndMoment = new(2026, 11, 3, 16, 59, 0, TimeSpan.Zero);

    // Câu thông báo khớp ví dụ của `customer-api.json`. Test khóa nguyên câu, đổi câu trong code thì test đỏ.
    private const string LimitMessage = "Bạn đã gửi 3 yêu cầu gọi lại hôm nay. CSKH sẽ liên hệ sớm.";

    private readonly FakeTimeProvider _clock = new(DayEndMoment);
    // Rào chắn giữ bốn request ở bước đọc hội thoại, để cả bốn cùng bước vào bước khóa và đếm.
    private readonly ParallelOwnershipReadGate _gate = new(expectedReaders: 4);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
            services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(_gate)));
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
        await CallbackRequestTestKit.DeleteIssuedCallbackRequestsAsync(database);
    }

    [Fact]
    // Yêu cầu thứ 4 trong cùng ngày giờ Việt Nam trả 429. Yêu cầu bị chặn không lưu dòng và không đóng phiên chờ.
    public async Task Post_FourthRequestInSameVietnamDay_Returns429AndStoresNothing()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        var anonymousId = SupportConversationTestKit.ReadAnonymousId(cookie);

        for (var index = 0; index < 3; index++)
        {
            var accepted = await PostAsync(cookie, conversation);
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        // Phiên chờ mở sau lượt thứ ba phải còn mở khi lượt thứ tư bị từ chối.
        var waitingSession = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, _clock.GetUtcNow().UtcDateTime);

        var rejected = await PostAsync(cookie, conversation);

        Assert.Equal((HttpStatusCode)429, rejected.StatusCode);
        var rejectedText = await rejected.Content.ReadAsStringAsync();
        SupportDeskContract.AssertMatches(
            JsonNode.Parse(rejectedText), rejected.StatusCode, "customer-api.json", "yêu cầu gọi lại thứ 4 trong ngày");
        using var document = System.Text.Json.JsonDocument.Parse(rejectedText);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("CALLBACK_LIMIT_REACHED", error.GetProperty("code").GetString());
        Assert.Equal(LimitMessage, error.GetProperty("message").GetString());
        // Chỉ ba dòng đầu được lưu. Yêu cầu thứ tư bị chặn hoàn toàn.
        Assert.Equal(3, (await CallbackRequestTestKit.ReadCallbackRequestsAsync(database, anonymousId)).Count);
        var stillOpen = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Equal(waitingSession.Id, stillOpen.Id);
        Assert.Null(stillOpen.ClosedAt);
        Assert.Null(stillOpen.ClosedBy);
    }

    [Fact]
    // Nửa đêm giờ Việt Nam là 17:00 UTC. Qua ngày mới thì bộ đếm về 0, nên yêu cầu lại được nhận.
    public async Task Post_AfterVietnamMidnight_AcceptsTheRequestAgain()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        for (var index = 0; index < 3; index++)
        {
            await PostAsync(cookie, conversation);
        }

        // 17:01 UTC là 00:01 ngày 2026-11-04 theo giờ Việt Nam.
        _clock.Advance(TimeSpan.FromMinutes(2));
        var response = await PostAsync(cookie, conversation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    // Bốn request song song từ bảng rỗng: đúng ba request 201 và một request 429. Khóa giao dịch giữ cho hai request không cùng vượt giới hạn.
    public async Task Post_FourParallelRequests_ExactlyThreeSucceed()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        var anonymousId = SupportConversationTestKit.ReadAnonymousId(cookie);

        // Bật rào trước khi phát request, để cả bốn request bị giữ ở bước đọc hội thoại rồi thả cùng lúc.
        _gate.Arm();
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => PostAsync(cookie, conversation)));

        Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Single(responses, response => response.StatusCode == (HttpStatusCode)429);
        Assert.Equal(3, (await CallbackRequestTestKit.ReadCallbackRequestsAsync(database, anonymousId)).Count);
        // Cả bốn request đã cùng qua bước đọc hội thoại, nên tranh chấp thật sự xảy ra ở bước khóa và đếm.
        Assert.True(_gate.AllReadersPassed, "Rào chắn không chờ đủ bốn bộ đọc hội thoại.");
    }

    private async Task<SupportConversation> SeedConversationAsync(string cookie)
    {
        return await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
    }

    private Task<HttpResponseMessage> PostAsync(string cookie, SupportConversation conversation)
    {
        return _client.SendAsync(
            SupportConversationTestKit.Post(
                CallbackRequestsPath, cookie, new { conversationId = conversation.Id, phone = "0901234567" }));
    }

    // Giữ bước đọc hội thoại của mọi request tới khi đủ số bộ đọc mong muốn, rồi thả cùng lúc.
    // Gate chỉ hoạt động sau khi test gọi `Arm`, nên các test tuần tự trong cùng class không bị giữ.
    private sealed class ParallelOwnershipReadGate : DbCommandInterceptor
    {
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);

        private readonly int _expectedReaders;
        private readonly HashSet<DbContext> _countedContexts = [];
        private readonly Lock _lock = new();
        private TaskCompletionSource _allReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;
        private int _readerCount;

        public ParallelOwnershipReadGate(int expectedReaders)
        {
            _expectedReaders = expectedReaders;
        }

        // Cả bốn bộ đọc đã qua rào, nên các request đã cùng bước vào bước khóa và đếm.
        public bool AllReadersPassed => Volatile.Read(ref _readerCount) >= _expectedReaders;

        // Bật rào ngay trước khi phát các request song song. Test tuần tự không gọi hàm này.
        public void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 0)
            {
                return result;
            }

            // Chỉ câu đọc hội thoại kiểm chủ bị giữ. Các câu đọc khác của request không bị ảnh hưởng.
            var isOwnershipRead = command.CommandText.Contains("FROM support_conversations", StringComparison.Ordinal);
            if (!isOwnershipRead || eventData.Context is null)
            {
                return result;
            }

            // Bộ đọc cuối thả cả nhóm. Hết hạn chờ thì ném lỗi để test đỏ thay vì treo.
            if (ReachExpectedReaders(eventData.Context) is { } release)
            {
                await release.WaitAsync(MaxWait, cancellationToken);
            }

            return result;
        }

        // Trả task phải chờ, hoặc `null` khi bộ đọc này là bộ cuối và thả cả nhóm.
        private Task? ReachExpectedReaders(DbContext context)
        {
            lock (_lock)
            {
                // Mỗi `DbContext` chỉ tính một lần, vì một request có thể đọc lại bảng hội thoại.
                if (!_countedContexts.Add(context))
                {
                    return null;
                }

                _readerCount++;
                if (_readerCount < _expectedReaders)
                {
                    return _allReleased.Task;
                }

                // Đủ số bộ đọc. Thả nhóm hiện tại và dựng vòng mới cho lượt chạy kế tiếp.
                var released = _allReleased;
                _allReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                released.TrySetResult();
                return null;
            }
        }
    }
}
