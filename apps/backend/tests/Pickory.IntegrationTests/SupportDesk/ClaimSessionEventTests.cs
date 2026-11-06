using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Modules.SupportDesk.Sessions;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra sự kiện SSE khi nhân viên nhận phiên hỗ trợ cho khách và nhân viên khác (SD-06).
[Collection(SupportDeskCollection.Name)]
public sealed class ClaimSessionEventTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private const string StaffEventsPath = "/api/admin/support/events";
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;
    private SupportEventHub _staffHub = null!;
    private CustomerEventHub _customerHub = null!;

    public async Task InitializeAsync()
    {
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
        await SupportDeskTestKit.ClearPresenceAsync(database);
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    [Fact]
    // Khi nhận phiên, khách nhận session.assigned và tin nhắn hệ thống, nhân viên khác nhận session.assigned.
    public async Task Claim_BroadcastsAssignedAndSystemMessageToCustomer_AndAssignedToOtherStaff()
    {
        // Tạo nhân viên 1 là người nhận phiên.
        var (_, staffToken1) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "sse-claim-staff1");
        // Tạo nhân viên 2 có kết nối SSE để theo dõi sự kiện gỡ phiên khỏi tab Đang chờ.
        var (_, staffToken2) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "sse-claim-staff2");

        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(guestCookie));
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, TestMoment.UtcDateTime.AddMinutes(-5), handoffInHours: true);

        // Mở luồng SSE phía khách cho đúng hội thoại.
        var customerEventsUrl = $"{SupportConversationTestKit.ConversationsPath}/{conversation.Id}/events";
        using var customerStream = await SseTestStream.OpenAsync(_client, customerEventsUrl, guestCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_customerHub, 1);

        // Mở luồng SSE phía nhân viên 2.
        using var staffStream = await SseTestStream.OpenAsync(_client, StaffEventsPath, $"pk_session={staffToken2}");
        await SupportDeskTestKit.WaitForConnectionCountAsync(_staffHub, 1);

        // Nhân viên 1 gửi yêu cầu nhận phiên qua API.
        var claimReq = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/assignment",
            body: null,
            $"pk_session={staffToken1}");
        var claimRes = await _client.SendAsync(claimReq);
        Assert.Equal(HttpStatusCode.Created, claimRes.StatusCode);

        // Khách hàng nhận được sự kiện session.assigned báo nhân viên đang hỗ trợ.
        var customerAssigned = await customerStream.ReadEventNamedAsync("session.assigned");
        Assert.Equal(session.Id, customerAssigned.Data.GetProperty("sessionId").GetString());

        // Khách hàng nhận được sự kiện message.created chứa tin nhắn hệ thống thông báo.
        var customerMessage = await customerStream.ReadEventNamedAsync("message.created");
        Assert.Equal("system", customerMessage.Data.GetProperty("role").GetString());
        Assert.Equal(SupportSystemMessages.SessionClaimed, customerMessage.Data.GetProperty("body").GetString());

        // Nhân viên khác nhận được sự kiện session.assigned để gỡ phiên khỏi tab Đang chờ.
        var staffAssigned = await staffStream.ReadEventNamedAsync("session.assigned");
        Assert.Equal(session.Id, staffAssigned.Data.GetProperty("sessionId").GetString());
    }
}
