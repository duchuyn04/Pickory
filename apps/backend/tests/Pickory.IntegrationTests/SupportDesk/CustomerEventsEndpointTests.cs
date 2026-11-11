using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-16: `GET /api/support/conversations/{id}/events`. Test mở luồng SSE thật của host và chỉ đẩy sự kiện qua đường producer.
[Collection(SupportDeskCollection.Name)]
public sealed class CustomerEventsEndpointTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    // Ghi thẳng 2 giây theo tiêu chí SD-16, không đọc hằng của code. Code gửi chậm hơn thì test phải đỏ.
    private static readonly TimeSpan StaffMessageDeadline = TimeSpan.FromSeconds(2);

    // Ghi thẳng 15 giây theo tiêu chí SD-16, không đọc hằng của code. Đổi chu kỳ trong code thì test phải đỏ.
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    private readonly FakeTimeProvider _clock = new(SupportDeskTestKit.NowAtMillisecondPrecision());
    // Handler thử ghi lại việc dòng tin đã đọc được lúc sự kiện được phát. Test thứ tự commit dùng số này.
    private readonly CommittedMessageProbe _messageProbe = new(database);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;
    private CustomerEventHub _hub = null!;

    public async Task InitializeAsync()
    {
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
        {
            // Đăng ký handler thử cùng dispatcher thật, nên nó chạy đúng lúc host phát sự kiện.
            services.AddSingleton(_messageProbe);
            services.AddSingleton<IDomainEventHandler<SupportMessageCreatedEvent>>(
                provider => provider.GetRequiredService<CommittedMessageProbe>());
        });
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _hub = _host.Services.GetRequiredService<CustomerEventHub>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    [Fact]
    // Người khác không được mở kênh của hội thoại người ta. Hub không giữ kết nối nào sau 404.
    public async Task Get_AnotherGuest_Returns404AndDoesNotRegisterConnection()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(ownerCookie);
        var strangerCookie = SupportConversationTestKit.NewGuestCookie();

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), strangerCookie);

        await SupportConversationTestKit.AssertErrorAsync(
            stream.Response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        Assert.Equal(0, _hub.ConnectionCount);
    }

    [Fact]
    // Khách đăng nhập khác tài khoản cũng nhận 404, không phải 403. Không dò được hội thoại nào tồn tại.
    public async Task Get_LoggedInCustomerWhoDoesNotOwnTheConversation_Returns404()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(ownerCookie);
        var (_, customerCookie) = await SupportConversationTestKit.CreateCustomerAsync(database);

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), customerCookie);

        await SupportConversationTestKit.AssertErrorAsync(
            stream.Response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        Assert.Equal(0, _hub.ConnectionCount);
    }

    [Fact]
    // Chủ cũ đăng xuất, người khác đăng nhập trên cùng trình duyệt. `pk_anon` không đổi nhưng hội thoại của chủ cũ bị chặn.
    public async Task Get_AccountBOnTheBrowserOfAccountA_Returns404()
    {
        var sharedBrowserCookie = SupportConversationTestKit.NewGuestCookie();
        var (accountA, _) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database,
            SupportConversationTestKit.ReadAnonymousId(sharedBrowserCookie),
            customerUserId: accountA.Id);
        var (_, accountBCookie) = await SupportConversationTestKit.CreateCustomerAsync(database, sharedBrowserCookie);

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), accountBCookie);

        await SupportConversationTestKit.AssertErrorAsync(
            stream.Response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
    }

    [Fact]
    // Mã hội thoại không tồn tại và hội thoại của người khác trả cùng nội dung 404.
    public async Task Get_UnknownConversationId_Returns404WithSameBodyAsForeignConversation()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var foreignConversation = await SeedConversationAsync(ownerCookie);
        var callerCookie = SupportConversationTestKit.NewGuestCookie();

        using var unknownStream = await SseTestStream.OpenAsync(_client, EventsPath(Guid.NewGuid().ToString()), callerCookie);
        using var foreignStream = await SseTestStream.OpenAsync(_client, EventsPath(foreignConversation.Id), callerCookie);

        var unknownBody = await SupportConversationTestKit.AssertErrorAsync(
            unknownStream.Response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        var foreignBody = await SupportConversationTestKit.AssertErrorAsync(
            foreignStream.Response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        // So sánh nguyên văn nội dung lỗi để người ngoài không phân biệt được hội thoại có tồn tại hay không.
        Assert.Equal(unknownBody, foreignBody);
        Assert.Equal(0, _hub.ConnectionCount);
    }

    [Fact]
    // Nhân viên gọi route phía khách nhận 403, giống ba route hội thoại của SD-15.
    public async Task Get_Staff_Returns403AndDoesNotRegisterConnection()
    {
        var ownerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(ownerCookie);
        var staffCookie = await SupportConversationTestKit.CreateStaffCookieAsync(database);

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), staffCookie);

        await SupportConversationTestKit.AssertErrorAsync(stream.Response, HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(0, _hub.ConnectionCount);
    }

    [Fact]
    public async Task Get_Owner_Returns200WithTheStreamingHeaders()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);

        Assert.Equal(HttpStatusCode.OK, stream.Response.StatusCode);
        Assert.Equal("text/event-stream", stream.Response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", Assert.Single(stream.Response.Headers.GetValues("Cache-Control")));
        Assert.Equal("no", Assert.Single(stream.Response.Headers.GetValues("X-Accel-Buffering")));
    }

    [Fact]
    // Khách đăng nhập sở hữu hội thoại theo tài khoản, nên mở được kênh bằng cookie phiên.
    public async Task Get_LoggedInCustomerOwner_Returns200()
    {
        var anonymousCookie = SupportConversationTestKit.NewGuestCookie();
        var (user, customerCookie) = await SupportConversationTestKit.CreateCustomerAsync(database, anonymousCookie);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database,
            SupportConversationTestKit.ReadAnonymousId(anonymousCookie),
            customerUserId: user.Id);

        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), customerCookie);

        Assert.Equal(HttpStatusCode.OK, stream.Response.StatusCode);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);
    }

    [Fact]
    public async Task Get_Owner_RegistersOneConnectionAndRemovesItWhenTheClientDisconnects()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);
        stream.Dispose();

        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 0);
    }

    [Fact]
    // Tiêu chí: tin của nhân viên tới kết nối SSE trong 2 giây sau commit. Hạn chờ đọc là 2 giây thật.
    public async Task Stream_StaffMessageAfterCommit_ReachesTheConnectionWithinTwoSeconds()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        var message = SupportEventTestKit.NewMessage(
            conversation.Id,
            SupportMessageRole.Staff,
            "Đơn của bạn đang được giao.",
            action: null,
            createdAt: _clock.GetUtcNow().UtcDateTime);
        await SupportEventTestKit.SaveMessageAfterCommitAsync(database, _host.Services, message);

        var received = await stream.ReadEventNamedAsync("message.created", StaffMessageDeadline);

        // Khóa đúng tập trường công khai. Thiếu `staffId` và ghi chú nội bộ, vì kênh này dành cho khách.
        Assert.Equal(
            ["id", "role", "body", "action", "staffDisplayName", "createdAt"],
            received.Data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(message.Id, received.Data.GetProperty("id").GetString());
        Assert.Equal("staff", received.Data.GetProperty("role").GetString());
        Assert.Equal("Đơn của bạn đang được giao.", received.Data.GetProperty("body").GetString());
        Assert.Equal(JsonValueKind.Null, received.Data.GetProperty("action").ValueKind);
        Assert.Equal(JsonValueKind.Null, received.Data.GetProperty("staffDisplayName").ValueKind);
        Assert.Equal(message.CreatedAt, received.Data.GetProperty("createdAt").GetDateTime().ToUniversalTime());
        // Tin đọc được từ kết nối khác, nên sự kiện chỉ phát sau khi transaction commit.
        Assert.Single(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
    }

    [Fact]
    // Tin hệ thống mang `action` để trang `/support` mở đúng form của SD-UI6.
    public async Task Stream_SystemMessageAfterCommit_ReachesTheConnectionWithItsAction()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        var message = SupportEventTestKit.NewMessage(
            conversation.Id,
            SupportMessageRole.System,
            "Hiện tại đang ngoài giờ hỗ trợ. Bạn để lại số điện thoại nhé.",
            action: "callback_form",
            createdAt: _clock.GetUtcNow().UtcDateTime);
        await SupportEventTestKit.SaveMessageAfterCommitAsync(database, _host.Services, message);

        var received = await stream.ReadEventNamedAsync("message.created");

        Assert.Equal(
            ["id", "role", "body", "action", "staffDisplayName", "createdAt"],
            received.Data.EnumerateObject().Select(property => property.Name));
        Assert.Equal("system", received.Data.GetProperty("role").GetString());
        Assert.Equal("callback_form", received.Data.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, received.Data.GetProperty("staffDisplayName").ValueKind);
    }

    [Fact]
    // Tin của khách không phát ngược qua kênh khách. Khách đã nhận tin trong response gửi tin và lịch sử.
    public async Task Stream_CustomerMessageEvent_IsNotSentBackToTheCustomerChannel()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        var message = SupportEventTestKit.NewMessage(
            conversation.Id,
            SupportMessageRole.Customer,
            "Tin của khách.",
            action: null,
            createdAt: _clock.GetUtcNow().UtcDateTime);
        await SupportEventTestKit.SaveMessageAfterCommitAsync(database, _host.Services, message);

        // Dòng đánh dấu phát sau sự kiện của tin khách. Tin khách lọt ra thì nó đứng trước dấu.
        _hub.Broadcast(conversation.Id, new SupportEvent("test.marker", new { }));

        var first = await stream.ReadEventAsync();
        Assert.Equal("test.marker", first.Name);
    }

    [Fact]
    // Ba trạng thái phiên dùng đúng ba tên sự kiện. Dữ liệu công khai chỉ có `sessionId`.
    public async Task Stream_SessionTransitionsAfterCommit_ReachTheConnectionWithTheSessionId()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        var session = await SupportEventTestKit.OpenSessionAfterCommitAsync(
            database, _host.Services, conversation, _clock.GetUtcNow().UtcDateTime);
        var waiting = await stream.ReadEventNamedAsync("session.waiting");
        Assert.Equal(["sessionId"], waiting.Data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(session.Id, waiting.Data.GetProperty("sessionId").GetString());

        await SupportEventTestKit.ClaimSessionAfterCommitAsync(
            database, _host.Services, session.Id, "staff-1", _clock.GetUtcNow().UtcDateTime);
        var assigned = await stream.ReadEventNamedAsync("session.assigned");
        Assert.Equal(["sessionId"], assigned.Data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(session.Id, assigned.Data.GetProperty("sessionId").GetString());

        await SupportEventTestKit.CloseSessionAfterCommitAsync(
            database, _host.Services, session.Id, "staff-1", _clock.GetUtcNow().UtcDateTime);
        var closed = await stream.ReadEventNamedAsync("session.closed");
        Assert.Equal(["sessionId"], closed.Data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(session.Id, closed.Data.GetProperty("sessionId").GetString());
    }

    [Fact]
    // Kênh chỉ phát cho đúng hội thoại. Hai hội thoại mở cùng lúc không thấy sự kiện của nhau.
    public async Task Stream_EventOfAnotherConversation_DoesNotReachTheConnection()
    {
        var cookieA = SupportConversationTestKit.NewGuestCookie();
        var conversationA = await SeedConversationAsync(cookieA);
        var cookieB = SupportConversationTestKit.NewGuestCookie();
        var conversationB = await SeedConversationAsync(cookieB);
        using var streamA = await SseTestStream.OpenAsync(_client, EventsPath(conversationA.Id), cookieA);
        using var streamB = await SseTestStream.OpenAsync(_client, EventsPath(conversationB.Id), cookieB);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 2);

        await SupportEventTestKit.SaveMessageAfterCommitAsync(
            database,
            _host.Services,
            SupportEventTestKit.NewMessage(
                conversationA.Id,
                SupportMessageRole.Staff,
                "Tin cho A.",
                action: null,
                createdAt: _clock.GetUtcNow().UtcDateTime));
        await SupportEventTestKit.SaveMessageAfterCommitAsync(
            database,
            _host.Services,
            SupportEventTestKit.NewMessage(
                conversationB.Id,
                SupportMessageRole.Staff,
                "Tin cho B.",
                action: null,
                createdAt: _clock.GetUtcNow().UtcDateTime));

        // Sự kiện đầu tiên của B phải là tin của B. Tin của A lọt sang thì thân tin sẽ là "Tin cho A.".
        var firstForB = await streamB.ReadEventAsync(StaffMessageDeadline);
        Assert.Equal("message.created", firstForB.Name);
        Assert.Equal("Tin cho B.", firstForB.Data.GetProperty("body").GetString());
        var firstForA = await streamA.ReadEventAsync(StaffMessageDeadline);
        Assert.Equal("Tin cho A.", firstForA.Data.GetProperty("body").GetString());
    }

    [Fact]
    // Tiêu chí: sự kiện chỉ gửi sau commit. Handler đọc dòng tin bằng kết nối khác phải thấy tin khi nhận sự kiện.
    public async Task Stream_MessageEventIsPublishedOnlyAfterCommit()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        var message = SupportEventTestKit.NewMessage(
            conversation.Id,
            SupportMessageRole.Staff,
            "Tin đã commit.",
            action: null,
            createdAt: _clock.GetUtcNow().UtcDateTime);
        await SupportEventTestKit.SaveMessageAfterCommitAsync(database, _host.Services, message);
        await stream.ReadEventNamedAsync("message.created", StaffMessageDeadline);

        // Phát trước commit thì handler mở kết nối khác không thấy dòng tin chưa commit.
        Assert.True(_messageProbe.ReadVisibility(message.Id));
    }

    [Fact]
    // Dòng đánh dấu đi trước commit phải đến trước sự kiện của tin, nên luồng không nhận tin khi transaction còn mở.
    public async Task Stream_MessageSavedInAnOpenTransaction_IsSentOnlyAfterCommit()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        var message = SupportEventTestKit.NewMessage(
            conversation.Id,
            SupportMessageRole.Staff,
            "Tin chờ commit.",
            action: null,
            createdAt: _clock.GetUtcNow().UtcDateTime);
        await SupportEventTestKit.SaveMessageAfterCommitAsync(
            database,
            _host.Services,
            message,
            afterSaveBeforeCommit: () =>
            {
                _hub.Broadcast(conversation.Id, new SupportEvent("test.marker", new { }));
                return Task.CompletedTask;
            });

        var first = await stream.ReadEventAsync();
        Assert.Equal("test.marker", first.Name);
        var received = await stream.ReadEventNamedAsync("message.created", StaffMessageDeadline);
        Assert.Equal("Tin chờ commit.", received.Data.GetProperty("body").GetString());
    }

    [Fact]
    // Tiêu chí: transaction bị hủy không sinh sự kiện. Dòng đánh dấu phát sau rollback phải đến trước mọi sự kiện.
    public async Task Stream_RolledBackMessage_IsNotSentAndTheMarkerArrivesFirst()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        var message = SupportEventTestKit.NewMessage(
            conversation.Id,
            SupportMessageRole.Staff,
            "Tin bị hủy.",
            action: null,
            createdAt: _clock.GetUtcNow().UtcDateTime);
        await SupportEventTestKit.SaveMessageAndRollbackAsync(database, _host.Services, message);

        _hub.Broadcast(conversation.Id, new SupportEvent("test.marker", new { }));

        var first = await stream.ReadEventAsync();
        Assert.Equal("test.marker", first.Name);
        Assert.Empty(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
    }

    [Fact]
    // Tiêu chí: kết nối nhận dòng chú thích giữ kết nối mỗi 15 giây.
    public async Task KeepAlive_CommentIsSentEveryFifteenSeconds()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        _clock.Advance(KeepAliveInterval);
        var firstLine = await stream.ReadCommentLineAsync();
        Assert.StartsWith(":", firstLine);

        _clock.Advance(KeepAliveInterval);
        var secondLine = await stream.ReadCommentLineAsync();
        Assert.StartsWith(":", secondLine);
    }

    [Fact]
    // Chưa tới 15 giây thì chưa gửi chú thích. Dòng đầu của luồng phải là sự kiện đánh dấu, không phải chú thích.
    public async Task KeepAlive_BeforeFifteenSeconds_IsNotSent()
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        using var stream = await SseTestStream.OpenAsync(_client, EventsPath(conversation.Id), cookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_hub, 1);

        _clock.Advance(KeepAliveInterval - TimeSpan.FromMilliseconds(1));
        _hub.Broadcast(conversation.Id, new SupportEvent("test.marker", new { }));

        var firstLine = await stream.ReadLineAsync();
        Assert.Equal("event: test.marker", firstLine);
    }

    private static string EventsPath(string conversationId) =>
        $"{SupportConversationTestKit.ConversationsPath}/{conversationId}/events";

    private Task<SupportConversation> SeedConversationAsync(string cookie) =>
        SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));
}
