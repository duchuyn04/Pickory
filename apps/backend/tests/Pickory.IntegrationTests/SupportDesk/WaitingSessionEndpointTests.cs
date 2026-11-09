using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-04: khách gửi tin khi hội thoại chưa có phiên mở thì module mở phiên chờ kèm tin hệ thống.
// Mọi test đặt đồng hồ giả ở một mốc cố định, để kết quả không phụ thuộc giờ chạy test.
[Collection(SupportDeskCollection.Name)]
public sealed class WaitingSessionEndpointTests(SupportDeskWebApplicationFactory factory, SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string InHoursPrefix = "Đã chuyển cho nhân viên CSKH. Thời gian chờ dự kiến khoảng ";
    private const string OutsideHoursBody =
        "Hiện ngoài giờ hỗ trợ (8:00 đến 21:00). Bạn để lại SĐT, CSKH sẽ gọi lại.";

    private readonly FakeTimeProvider _clock = new(SupportSessionTestKit.InHoursMoment);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // Bảng trực dùng chung cho cả bộ test. Xóa trước mỗi test để dòng `available` sót lại không đổi nhánh trong giờ.
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportSessionTestKit.ResetSettingsAsync(database);
        await SupportDeskTestKit.ClearPresenceAsync(database);
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    [Fact]
    public async Task Post_FirstMessageInHours_OpensWaitingSessionAndWritesTheEstimate()
    {
        await MarkStaffAvailableAsync();
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        var response = await SendMessageAsync(cookie, conversation, "Mình cần hỗ trợ.");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        // Phiên lưu mốc mở phiên từ đồng hồ của host, không lấy giờ máy chủ.
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, session.HandoffAt);
        Assert.True(session.HandoffInHours);
        // Hai cột định danh chép từ hội thoại lúc mở phiên.
        Assert.Equal(conversation.CustomerUserId, session.CustomerUserId);
        Assert.Equal(conversation.AnonymousId, session.AnonymousId);
        Assert.Null(session.StaffId);
        Assert.Null(session.NudgedAt);

        var messages = await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id);
        Assert.Equal([SupportMessageRole.Customer, SupportMessageRole.System], messages.Select(message => message.Role));
        // Chưa có phiên nào được nhận trong 7 ngày nên mỗi lượt tính 5 phút. Một nhân viên sẵn sàng và không có ai chờ trước.
        Assert.Equal($"{InHoursPrefix}5 phút.", messages[1].Body);
        // Tin trong giờ không mở form, vì khách chỉ cần chờ nhân viên nhận.
        Assert.Null(messages[1].Action);
        // Tin hệ thống ghi sau tin của khách một mili giây, nên lịch sử xếp tin của khách trước.
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddMilliseconds(1), messages[1].CreatedAt);
    }

    [Fact]
    public async Task Post_FirstMessageOfLoggedInCustomer_StoresTheAccountAsOwnerOfTheSession()
    {
        await MarkStaffAvailableAsync();
        var (user, cookie) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie.Split("; ")[1]), customerUserId: user.Id);

        await SendMessageAsync(cookie, conversation, "Mình cần hỗ trợ.");

        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        // Phiên của khách đăng nhập mang mã tài khoản, nên xóa dữ liệu theo `UserDeletedEvent` tìm được dòng này.
        Assert.Equal(user.Id, session.CustomerUserId);
        Assert.Equal(conversation.AnonymousId, session.AnonymousId);
    }

    [Fact]
    public async Task Post_FirstMessageInHours_CountsTheOtherWaitingSessionsInTheEstimate()
    {
        await MarkStaffAvailableAsync();
        var otherCookie = SupportConversationTestKit.NewGuestCookie();
        var otherConversation = await SeedConversationAsync(otherCookie);
        // Một khách khác đang chờ người nhận. Khách mới đứng sau phiên đó nên chờ hai lượt, mỗi lượt 5 phút.
        await SupportSessionTestKit.SeedSessionAsync(
            database, otherConversation, _clock.GetUtcNow().UtcDateTime.AddMinutes(-2));
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        await SendMessageAsync(cookie, conversation, "Mình cần hỗ trợ.");

        var systemMessage = Assert.Single(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.System));
        Assert.Equal($"{InHoursPrefix}10 phút.", systemMessage.Body);
    }

    [Fact]
    public async Task Post_FirstMessageInHours_UsesTheAverageWaitOfTheLastSevenDays()
    {
        await MarkStaffAvailableAsync();
        // Hai phiên được nhận sau 3 phút trong 7 ngày qua, nên mỗi lượt tính 3 phút.
        await SeedClaimedSessionAsync(TimeSpan.FromMinutes(3), claimedBefore: TimeSpan.FromDays(1));
        await SeedClaimedSessionAsync(TimeSpan.FromMinutes(3), claimedBefore: TimeSpan.FromHours(5));
        // Phiên nhận cách đây 30 ngày nằm ngoài cửa sổ 7 ngày. Thời gian chờ 60 phút của nó không được tính.
        await SeedClaimedSessionAsync(TimeSpan.FromMinutes(60), claimedBefore: TimeSpan.FromDays(30));
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        await SendMessageAsync(cookie, conversation, "Mình cần hỗ trợ.");

        var systemMessage = Assert.Single(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.System));
        Assert.Equal($"{InHoursPrefix}3 phút.", systemMessage.Body);
    }

    [Fact]
    public async Task Post_TenMessagesInHours_AreLimitedByCustomerMessagesOnly()
    {
        await MarkStaffAvailableAsync();
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        for (var index = 1; index <= 10; index++)
        {
            Assert.Equal(
                HttpStatusCode.Created,
                (await SendMessageAsync(cookie, conversation, $"Tin {index}")).StatusCode);
        }

        var blocked = await SendMessageAsync(cookie, conversation, "Tin thứ mười một");

        // Tin hệ thống của phiên chờ không tính vào giới hạn gửi tin. Khách vẫn gửi đủ 10 tin mỗi phút.
        await SupportConversationTestKit.AssertErrorAsync(blocked, HttpStatusCode.TooManyRequests, "SUPPORT_MESSAGE_RATE_LIMITED");
        Assert.Equal(
            10,
            (await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.Customer)).Count);
        Assert.Single(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.System));
    }

    [Fact]
    public async Task Post_FirstMessageAtNinePmVietnam_WritesTheOutsideHoursMessageWithCallbackForm()
    {
        // Đúng giờ đóng cửa là ngoài giờ. 21:00 giờ Việt Nam vẫn nhận tin ngoài giờ.
        _clock.SetUtcNow(SupportSessionTestKit.ClosingMoment);
        await MarkStaffAvailableAsync();
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        await SendMessageAsync(cookie, conversation, "Mình cần hỗ trợ.");

        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.False(session.HandoffInHours);
        var systemMessage = Assert.Single(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.System));
        Assert.Equal(OutsideHoursBody, systemMessage.Body);
        // Trang `/support` dựa vào `action` để mở form số điện thoại cho khách.
        Assert.Equal("callback_form", systemMessage.Action);
    }

    [Fact]
    public async Task Post_FirstMessageOnHoliday_WritesTheOutsideHoursMessage()
    {
        await SupportSessionTestKit.UpdateSettingsAsync(
            database, holidays: [SupportSessionTestKit.VietnamDate(SupportSessionTestKit.InHoursMoment)]);
        await MarkStaffAvailableAsync();
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        await SendMessageAsync(cookie, conversation, "Mình cần hỗ trợ.");

        // Ngày nghỉ là ngoài giờ cả ngày, kể cả lúc nằm giữa khung giờ và có nhân viên sẵn sàng.
        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.False(session.HandoffInHours);
        var systemMessage = Assert.Single(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.System));
        Assert.Equal(OutsideHoursBody, systemMessage.Body);
        Assert.Equal("callback_form", systemMessage.Action);
    }

    [Fact]
    public async Task Post_FirstMessageInHoursWithoutAvailableStaff_WritesTheOutsideHoursMessage()
    {
        // Nhân viên đang tạm vắng. Trong khung giờ mà không ai nhận được phiên thì khách phải thấy thông báo ngoài giờ.
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, "staff-1", StaffPresenceStatus.Away, _clock.GetUtcNow());
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        await SendMessageAsync(cookie, conversation, "Mình cần hỗ trợ.");

        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.False(session.HandoffInHours);
        var systemMessage = Assert.Single(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.System));
        Assert.Equal(OutsideHoursBody, systemMessage.Body);
        Assert.Equal("callback_form", systemMessage.Action);
    }

    [Fact]
    public async Task Post_FirstMessageAtNinePmWithLaterClosingTime_UsesTheSettingsInsteadOfTheDefault()
    {
        // Admin dời giờ đóng cửa sang 22:00, nên 21:00 là trong giờ. Test chứng minh module đọc khung giờ từ DB.
        await SupportSessionTestKit.UpdateSettingsAsync(database, closesAt: new TimeOnly(22, 0));
        _clock.SetUtcNow(SupportSessionTestKit.ClosingMoment);
        await MarkStaffAvailableAsync();
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);

        await SendMessageAsync(cookie, conversation, "Mình cần hỗ trợ.");

        var session = Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.True(session.HandoffInHours);
        var systemMessage = Assert.Single(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.System));
        Assert.Equal($"{InHoursPrefix}5 phút.", systemMessage.Body);
        Assert.Null(systemMessage.Action);
    }

    [Fact]
    public async Task Post_SecondMessageWithOpenSession_OpensNoNewSessionAndWritesNoSecondSystemMessage()
    {
        await MarkStaffAvailableAsync();
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        await SendMessageAsync(cookie, conversation, "Tin thứ nhất");

        var response = await SendMessageAsync(cookie, conversation, "Tin thứ hai");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Hội thoại đã có phiên mở nên tin thứ hai chỉ vào phiên đó, không mở thêm phiên.
        Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Equal(
            2,
            (await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.Customer)).Count);
        // Chỉ tin đầu tiên mở phiên nên chỉ có một tin hệ thống.
        Assert.Single(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.System));
    }

    [Fact]
    public async Task Post_MessageAfterSessionIsClaimed_OpensNoNewSession()
    {
        await MarkStaffAvailableAsync();
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        // Phiên đã có nhân viên nhận nhưng chưa đóng. Khách vẫn nhắn trong phiên đó, không cần người nhận mới.
        await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-3),
            staffId: "staff-1",
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-2));

        await SendMessageAsync(cookie, conversation, "Mình bổ sung thêm thông tin.");

        Assert.Single(await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id));
        Assert.Empty(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(database, conversation.Id, SupportMessageRole.System));
    }

    [Fact]
    public async Task Post_MessageAfterThePreviousSessionClosed_OpensANewSession()
    {
        await MarkStaffAvailableAsync();
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        var previous = await SupportSessionTestKit.SeedSessionAsync(
            database,
            conversation,
            _clock.GetUtcNow().UtcDateTime.AddMinutes(-30),
            staffId: "staff-1",
            claimedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-28),
            closedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-20));

        await SendMessageAsync(cookie, conversation, "Mình cần hỗ trợ tiếp.");

        var sessions = await SupportSessionTestKit.ReadSessionsAsync(database, conversation.Id);
        // Phiên cũ đã đóng vẫn nằm trong hội thoại, và tin mới mở đúng một phiên chờ mới.
        Assert.Contains(previous.Id, sessions.Select(session => session.Id));
        var opened = Assert.Single(sessions, session => session.Id != previous.Id);
        Assert.Null(opened.ClosedAt);
        Assert.Null(opened.StaffId);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, opened.HandoffAt);
        // Chỉ phiên mới sinh tin hệ thống, nên hội thoại có đúng một tin hệ thống.
        Assert.Single(
            await SupportConversationTestKit.ReadMessagesByRoleAsync(
                database, conversation.Id, SupportMessageRole.System));
    }

    // Nhân viên sẵn sàng với nhịp tim vừa ghi. Điều kiện "có nhân viên sẵn sàng" của phiên trong giờ nhờ vậy thỏa.
    private Task MarkStaffAvailableAsync() =>
        SupportDeskTestKit.SetPresenceRowAsync(database, "staff-1", StaffPresenceStatus.Available, _clock.GetUtcNow());

    private Task<SupportConversation> SeedConversationAsync(string cookie) =>
        SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(cookie));

    private Task<HttpResponseMessage> SendMessageAsync(string cookie, SupportConversation conversation, string body) =>
        _client.SendAsync(SupportConversationTestKit.Post(
            SupportConversationTestKit.MessagesPath(conversation.Id), cookie, new { body }));

    // Gieo một phiên đã có nhân viên nhận. Mốc giờ tính từ đồng hồ của host, nên test không phụ thuộc giờ chạy.
    private async Task SeedClaimedSessionAsync(TimeSpan wait, TimeSpan claimedBefore)
    {
        var cookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SeedConversationAsync(cookie);
        var claimedAt = _clock.GetUtcNow().UtcDateTime - claimedBefore;
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, claimedAt - wait, staffId: "staff-1", claimedAt: claimedAt);
    }
}
