using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-15: `POST /api/support/conversations`. Route trả hội thoại `open` của người gọi, hoặc tạo mới.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class OpenConversationEndpointTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // Mốc mở phiên cố định của test. Khóa `createdAt` và `handoffAt` không phụ thuộc giờ chạy test.
    private static readonly DateTime HandoffAt = new(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => SupportConversationTestKit.DeleteIssuedConversationsAsync(database);

    [Fact]
    public async Task Post_NewGuest_Returns201WithRetentionNoticeAndStoresOpenConversation()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();

        var response = await _client.SendAsync(SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Hội thoại chứa dữ liệu cá nhân nên response không được lưu bản sao.
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.Equal(
            ["id", "status", "createdAt", "showRetentionNotice", "session"],
            data.EnumerateObject().Select(property => property.Name));
        Assert.Equal("open", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("showRetentionNotice").GetBoolean());
        // Hội thoại vừa tạo chưa có phiên nào, nên khách thấy `session` rỗng.
        Assert.Equal(JsonValueKind.Null, data.GetProperty("session").ValueKind);

        var stored = Assert.Single(await SupportConversationTestKit.ReadConversationsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie)));
        Assert.Equal(data.GetProperty("id").GetString(), stored.Id);
        Assert.Equal(SupportConversationStatus.Open, stored.Status);
        // Khách vãng lai chưa có tài khoản.
        Assert.Null(stored.CustomerUserId);
        Assert.Equal(stored.CreatedAt, stored.LastMessageAt);
    }

    [Fact]
    public async Task Post_SecondCallOfSameGuest_Returns200WithSameConversationAndNoNotice()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var first = await _client.SendAsync(SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));
        var firstData = await SupportConversationTestKit.ReadDataAsync(first);

        var second = await _client.SendAsync(SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondData = await SupportConversationTestKit.ReadDataAsync(second);
        Assert.Equal(firstData.GetProperty("id").GetString(), secondData.GetProperty("id").GetString());
        Assert.False(secondData.GetProperty("showRetentionNotice").GetBoolean());
        Assert.Single(await SupportConversationTestKit.ReadConversationsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie)));
    }

    [Fact]
    public async Task Post_GuestWhoseOnlyConversationIsClosed_CreatesNewConversation()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var closed = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie), status: SupportConversationStatus.Closed);

        var response = await _client.SendAsync(SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.NotEqual(closed.Id, data.GetProperty("id").GetString());
        Assert.True(data.GetProperty("showRetentionNotice").GetBoolean());
    }

    [Fact]
    public async Task Post_LoggedInCustomer_CreatesConversationOwnedByAccountAndReusesItFromAnotherBrowser()
    {
        var (user, cookie) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var firstResponse = await _client.SendAsync(SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));
        var firstData = await SupportConversationTestKit.ReadDataAsync(firstResponse);

        // Cùng tài khoản nhưng trình duyệt khác, nên `pk_anon` khác. Hội thoại theo tài khoản, không theo trình duyệt.
        var sessionPart = cookie.Split(';')[0];
        var otherBrowserCookie = $"{sessionPart}; {SupportConversationTestKit.NewGuestCookie()}";
        var secondResponse = await _client.SendAsync(
            SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, otherBrowserCookie));

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondData = await SupportConversationTestKit.ReadDataAsync(secondResponse);
        Assert.Equal(firstData.GetProperty("id").GetString(), secondData.GetProperty("id").GetString());
        var stored = await SupportConversationTestKit.ReadConversationAsync(database, firstData.GetProperty("id").GetString()!);
        Assert.Equal(user.Id, stored.CustomerUserId);
    }

    [Fact]
    public async Task Post_GuestSharingBrowserWithAccountThatOwnsOpenConversation_GetsOwnNewConversation()
    {
        // Chủ tài khoản đã đăng xuất nhưng `pk_anon` còn trong trình duyệt. Khách vãng lai sau đó không được thấy hội thoại của chủ.
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var accountConversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(guestCookie), customerUserId: Guid.NewGuid().ToString());

        var response = await _client.SendAsync(SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, guestCookie));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.NotEqual(accountConversation.Id, data.GetProperty("id").GetString());
        var stored = await SupportConversationTestKit.ReadConversationAsync(database, data.GetProperty("id").GetString()!);
        Assert.Null(stored.CustomerUserId);
    }

    [Fact]
    public async Task Post_Staff_Returns403AndWritesNothing()
    {
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);
        var staffWithAnonymousCookie = $"{staffCookie}; {SupportConversationTestKit.NewGuestCookie()}";

        var response = await _client.SendAsync(
            SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, staffWithAnonymousCookie));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Empty(await SupportConversationTestKit.ReadConversationsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(staffWithAnonymousCookie.Split("; ")[1])));
    }

    [Fact]
    public async Task Post_NewConversation_MatchesTheContractExample()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();

        var response = await _client.SendAsync(
            SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));

        // Ví dụ ghi `session` bằng `null` cho hội thoại vừa tạo.
        await AssertMatchesContractExampleAsync(response, "tạo hội thoại lần đầu");
    }

    [Fact]
    public async Task Post_ConversationWithWaitingSession_MatchesTheContractExample()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        var waitingSession = await SupportSessionTestKit.SeedSessionAsync(database, conversation, HandoffAt);

        var response = await _client.SendAsync(
            SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Ví dụ "mở lại hội thoại đang có phiên chờ" là tiêu chí của SD-04.
        await AssertMatchesContractExampleAsync(response, "mở lại hội thoại đang có phiên chờ");
        // Phép so hợp đồng bỏ qua giá trị `null`, nên test khóa thêm phiên thật mà response phải trả.
        var session = (await SupportConversationTestKit.ReadDataAsync(response)).GetProperty("session");
        Assert.Equal(waitingSession.Id, session.GetProperty("id").GetString());
        Assert.Equal("waiting", session.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Post_ConversationWithClaimedSession_ReportsAssignedStatus()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, HandoffAt, staffId: "staff-1", claimedAt: HandoffAt.AddSeconds(30));

        var response = await _client.SendAsync(
            SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));

        var session = (await SupportConversationTestKit.ReadDataAsync(response)).GetProperty("session");
        // Phiên có nhân viên giữ nhưng chưa đóng là trạng thái `assigned`.
        Assert.Equal("assigned", session.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("csatScore").ValueKind);
    }

    [Fact]
    public async Task Post_ConversationWithClosedSession_ReportsClosedStatusAndTheScore()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            HandoffAt,
            staffId: "staff-1",
            claimedAt: HandoffAt.AddSeconds(30),
            closedAt: HandoffAt.AddMinutes(5),
            csat: 4);

        var response = await _client.SendAsync(
            SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));

        var session = (await SupportConversationTestKit.ReadDataAsync(response)).GetProperty("session");
        // Phiên đã đóng thì trạng thái `closed`, kèm điểm khách đã chấm.
        Assert.Equal("closed", session.GetProperty("status").GetString());
        Assert.Equal(4, session.GetProperty("csatScore").GetInt32());
    }

    [Fact]
    public async Task Post_ConversationWithSeveralSessions_ReportsTheLatestByHandoffAt()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
        // Hội thoại có một phiên cũ đã đóng và một phiên chờ mới hơn. Đây là lần khách cần hỗ trợ thứ hai.
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, HandoffAt, staffId: "staff-1", closedAt: HandoffAt.AddMinutes(5), csat: 5);
        var newest = await SupportSessionTestKit.SeedSessionAsync(database, conversation, HandoffAt.AddHours(1));

        var response = await _client.SendAsync(
            SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie));

        var session = (await SupportConversationTestKit.ReadDataAsync(response)).GetProperty("session");
        Assert.Equal(newest.Id, session.GetProperty("id").GetString());
        Assert.Equal("waiting", session.GetProperty("status").GetString());
    }

    // Gửi request đọc hội thoại cho khách và so response với một ví dụ của `customer-api.json`.
    private static async Task AssertMatchesContractExampleAsync(HttpResponseMessage response, string exampleName)
    {
        // Phân tích chuỗi thô để helper so được cây JSON, giống dữ liệu đọc từ file mẫu.
        var responseBody = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(responseBody, response.StatusCode, "customer-api.json", exampleName);
    }

    [Fact]
    public async Task Post_TwoParallelRequestsOfSameGuest_CreateExactlyOneConversation()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        // Cổng chặn cả hai request ở bước đọc cho đến khi cả hai cùng thấy chưa có hội thoại. Nhờ vậy cả hai chắc chắn cùng đi vào bước tạo.
        var gate = new ConcurrentReadGate();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            // `ConfigureDbContext` thêm interceptor vào cấu hình thật của module. Không đăng ký lại `DbContext`, vì enum sẽ bị ánh xạ hai lần.
            services => services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(gate))));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var responses = await Task.WhenAll(
            client.SendAsync(SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie)),
            client.SendAsync(SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, cookie)));

        // Cả hai request đã đọc xong trước khi request nào tạo. Thiếu điều kiện này thì test không chứng minh được nhánh tranh chấp.
        Assert.True(gate.BothRequestsReadBeforeAnyInsert);
        // Kiểm theo tập hợp, vì không biết request nào thắng.
        Assert.Equivalent(new[] { HttpStatusCode.Created, HttpStatusCode.OK }, responses.Select(response => response.StatusCode).ToArray());
        var ids = new List<string>();
        foreach (var response in responses)
        {
            ids.Add((await SupportConversationTestKit.ReadDataAsync(response)).GetProperty("id").GetString()!);
        }

        Assert.Single(ids.Distinct());
        Assert.Single(await SupportConversationTestKit.ReadConversationsAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie)));
    }

    // Giữ truy vấn đọc đầu tiên của mỗi `DbContext`. Cổng thả khi đủ hai `DbContext` đã đọc, hoặc sau 10 giây.
    private sealed class ConcurrentReadGate : DbCommandInterceptor
    {
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);
        private readonly HashSet<DbContext> _contextsThatRead = [];
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BothRequestsReadBeforeAnyInsert => _bothRead.Task.IsCompletedSuccessfully;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            var isConversationRead = command.CommandText.Contains("FROM support_conversations", StringComparison.Ordinal);
            if (isConversationRead && eventData.Context is not null && RegisterFirstRead(eventData.Context))
            {
                await _bothRead.Task.WaitAsync(MaxWait, cancellationToken);
            }

            return result;
        }

        private bool RegisterFirstRead(DbContext context)
        {
            lock (_contextsThatRead)
            {
                var isFirstRead = _contextsThatRead.Add(context);
                if (isFirstRead && _contextsThatRead.Count == 2)
                {
                    _bothRead.TrySetResult();
                }

                return isFirstRead;
            }
        }
    }
}
