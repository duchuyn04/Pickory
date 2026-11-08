using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Realtime;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra sự kiện realtime SSE phía khách và phía nhân viên theo tiêu chí SD-07.
[Collection(SupportDeskCollection.Name)]
public sealed class SupportMessageRealtimeEventsTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly TimeSpan SseDeliveryDeadline = TimeSpan.FromSeconds(2);

    private static TimeSpan ReadRemainingDeadline(long startedAt)
    {
        var remaining = SseDeliveryDeadline - Stopwatch.GetElapsedTime(startedAt);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
    }
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 2, 17, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;
    private CustomerEventHub _customerHub = null!;
    private SupportEventHub _staffHub = null!;

    public Task InitializeAsync()
    {
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _customerHub = _host.Services.GetRequiredService<CustomerEventHub>();
        _staffHub = _host.Services.GetRequiredService<SupportEventHub>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
    }

    private static string CustomerEventsPath(string conversationId) =>
        $"/api/support/conversations/{conversationId}/events";

    private const string StaffEventsPath = "/api/admin/support/events";

    [Fact]
    // Khách nhận tin nhân viên qua SSE trong 2 giây kèm staffDisplayName.
    public async Task CustomerSse_StaffMessage_ReceivedWithinTwoSecondsWithDisplayName()
    {
        var (staff, staffToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "sse-staff");

        // Gán tên hồ sơ cho nhân viên trong Identity DB.
        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = "Lan - CSKH";
            await identity.SaveChangesAsync();
        }

        var customerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(customerCookie));

        var session = new SupportSession
        {
            ConversationId = conversation.Id,
            HandoffAt = TestMoment.UtcDateTime.AddMinutes(-5),
            HandoffInHours = true,
            AnonymousId = conversation.AnonymousId,
            StaffId = staff.Id,
            ClaimedAt = TestMoment.UtcDateTime.AddMinutes(-2),
        };
        await using (var context = database.CreateSupportDeskContext())
        {
            context.Sessions.Add(session);
            await context.SaveChangesAsync();
        }

        // Mở kết nối SSE của khách.
        using var customerStream = await SseTestStream.OpenAsync(
            _client, CustomerEventsPath(conversation.Id), customerCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_customerHub, 1);

        // Nhân viên gửi tin vào phiên.
        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/messages",
            new { body = "Chào bạn, mình kiểm tra ngay." },
            $"pk_session={staffToken}");
        // Đo từ trước lúc gửi tin. Tiêu chí tính từ lúc gửi tới lúc khách nhận được sự kiện.
        var sentAt = Stopwatch.GetTimestamp();
        var response = await _client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        // Khách phải nhận được sự kiện message.created trong hạn còn lại của 2 giây.
        var received = await customerStream.ReadEventNamedAsync("message.created", ReadRemainingDeadline(sentAt));
        var elapsed = Stopwatch.GetElapsedTime(sentAt);
        Assert.True(
            elapsed <= SseDeliveryDeadline,
            $"Khách nhận tin sau {elapsed.TotalSeconds:F2} giây, tiêu chí là 2 giây.");
        Assert.Equal("message.created", received.Name);
        var data = received.Data;
        Assert.Equal("staff", data.GetProperty("role").GetString());
        Assert.Equal("Chào bạn, mình kiểm tra ngay.", data.GetProperty("body").GetString());
        Assert.Equal("Lan - CSKH", data.GetProperty("staffDisplayName").GetString());
    }

    [Fact]
    // Hồ sơ nhân viên thiếu tên thì staffDisplayName gửi khách qua SSE bằng null.
    public async Task CustomerSse_StaffWithoutName_ReceivedWithNullDisplayNameAndNoEmail()
    {
        var (staff, staffToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "sse-noname");

        // Xóa tên hồ sơ để kiểm tra trường hợp chỉ có email.
        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = null;
            await identity.SaveChangesAsync();
        }

        var customerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(customerCookie));

        var session = new SupportSession
        {
            ConversationId = conversation.Id,
            HandoffAt = TestMoment.UtcDateTime.AddMinutes(-5),
            HandoffInHours = true,
            AnonymousId = conversation.AnonymousId,
            StaffId = staff.Id,
            ClaimedAt = TestMoment.UtcDateTime.AddMinutes(-2),
        };
        await using (var context = database.CreateSupportDeskContext())
        {
            context.Sessions.Add(session);
            await context.SaveChangesAsync();
        }

        using var customerStream = await SseTestStream.OpenAsync(
            _client, CustomerEventsPath(conversation.Id), customerCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_customerHub, 1);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/messages",
            new { body = "Chào bạn" },
            $"pk_session={staffToken}");
        var response = await _client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        var received = await customerStream.ReadEventNamedAsync("message.created", SseDeliveryDeadline);
        var rawJson = received.Data.GetRawText();
        Assert.DoesNotContain(staff.Email, rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Null, received.Data.GetProperty("staffDisplayName").ValueKind);
    }

    [Fact]
    // Tên hồ sơ chứa ký tự @ vẫn hiển thị chính xác trên kênh SSE của khách.
    public async Task CustomerSse_StaffMessageWithAtSign_ReceivedWithDisplayName()
    {
        var (staff, staffToken) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "sse-at");

        await using (var identity = database.CreateIdentityContext())
        {
            var user = await identity.Users.SingleAsync(u => u.Id == staff.Id);
            user.Name = "Lan @ CSKH";
            await identity.SaveChangesAsync();
        }

        var customerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(customerCookie));

        var session = new SupportSession
        {
            ConversationId = conversation.Id,
            HandoffAt = TestMoment.UtcDateTime.AddMinutes(-5),
            HandoffInHours = true,
            AnonymousId = conversation.AnonymousId,
            StaffId = staff.Id,
            ClaimedAt = TestMoment.UtcDateTime.AddMinutes(-2),
        };
        await using (var context = database.CreateSupportDeskContext())
        {
            context.Sessions.Add(session);
            await context.SaveChangesAsync();
        }

        using var customerStream = await SseTestStream.OpenAsync(
            _client, CustomerEventsPath(conversation.Id), customerCookie);
        await SupportDeskTestKit.WaitForConnectionCountAsync(_customerHub, 1);

        var request = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/messages",
            new { body = "Chào bạn" },
            $"pk_session={staffToken}");
        var response = await _client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        var received = await customerStream.ReadEventNamedAsync("message.created", SseDeliveryDeadline);
        Assert.Equal("Lan @ CSKH", received.Data.GetProperty("staffDisplayName").GetString());
    }

    [Fact]
    // Tin mới của khách gửi tới đúng nhân viên đang giữ phiên và nhân viên khác không nhận.
    public async Task StaffSse_CustomerMessage_ReachesOnlyAssignedStaff()
    {
        var (staffA, tokenA) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "staff-a");
        var (staffB, tokenB) = await IdentityTestData.CreateStaffSessionAsync(
            database, DefaultRoles.SupportCode, "staff-b");

        var customerCookie = SupportConversationTestKit.NewGuestCookie();
        var conversation = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(customerCookie));

        // Phiên do nhân viên A giữ.
        var session = new SupportSession
        {
            ConversationId = conversation.Id,
            HandoffAt = TestMoment.UtcDateTime.AddMinutes(-5),
            HandoffInHours = true,
            AnonymousId = conversation.AnonymousId,
            StaffId = staffA.Id,
            ClaimedAt = TestMoment.UtcDateTime.AddMinutes(-2),
        };
        await using (var context = database.CreateSupportDeskContext())
        {
            context.Sessions.Add(session);
            await context.SaveChangesAsync();
        }

        // Mở 2 kết nối SSE cho nhân viên A và nhân viên B.
        using var streamA = await SseTestStream.OpenAsync(_client, StaffEventsPath, $"pk_session={tokenA}");
        using var streamB = await SseTestStream.OpenAsync(_client, StaffEventsPath, $"pk_session={tokenB}");
        await SupportDeskTestKit.WaitForConnectionCountAsync(_staffHub, 2);

        // Khách gửi tin nhắn vào hội thoại.
        var customerRequest = SupportConversationTestKit.Post(
            $"/api/support/conversations/{conversation.Id}/messages",
            customerCookie,
            new { body = "Tin nhắn của khách" });
        var customerResponse = await _client.SendAsync(customerRequest);
        Assert.Equal(System.Net.HttpStatusCode.Created, customerResponse.StatusCode);

        // Nhân viên A phải nhận sự kiện message.created với đúng sessionId và conversationId.
        var eventForA = await streamA.ReadEventNamedAsync("message.created", SseDeliveryDeadline);
        Assert.Equal("message.created", eventForA.Name);
        Assert.Equal(
            ["sessionId", "conversationId"],
            eventForA.Data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(session.Id, eventForA.Data.GetProperty("sessionId").GetString());
        Assert.Equal(conversation.Id, eventForA.Data.GetProperty("conversationId").GetString());
        // Nhân viên B không được nhận sự kiện message.created này.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            streamB.ReadEventNamedAsync("message.created", TimeSpan.FromMilliseconds(400)));
    }
}
