using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// Kiểm tra hợp đồng API cho 4 ví dụ của admin-sessions.json theo tiêu chí SD-06.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AdminSupportSessionContractTests(
    PickoryWebApplicationFactory factory,
    IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string FixtureFileName = "admin-sessions.json";
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
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

    [Fact]
    // Hợp đồng hàng chờ waiting phải khớp hoàn toàn cấu trúc ví dụ hàng chờ.
    public async Task Contract_WaitingQueue_MatchesExample()
    {
        var (_, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-waiting");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var handoffAt = TestMoment.UtcDateTime.AddSeconds(-95);
        await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, handoffAt, handoffInHours: true);
        await SupportConversationTestKit.SeedMessageAsync(
            database, conversation.Id, "Đơn PK123 của mình giao tới đâu rồi?", handoffAt, SupportMessageRole.Customer);

        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, "/api/admin/support/sessions?view=waiting", $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // So khớp cấu trúc và kiểu dữ liệu của response với file mẫu hợp đồng.
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body, response.StatusCode, FixtureFileName, "hàng chờ");
    }

    [Fact]
    // Hợp đồng phiên của tôi sau chuyển giao phải khớp ví dụ phiên của tôi.
    public async Task Contract_MineQueue_MatchesExample()
    {
        var (staff, token) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-mine");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var handoffAt = TestMoment.UtcDateTime.AddMinutes(-20);
        var claimedAt = handoffAt.AddSeconds(40);
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, handoffAt, handoffInHours: true, staffId: staff.Id, claimedAt: claimedAt);

        // Cập nhật TransferNote vào database để khớp với ví dụ hợp đồng.
        await using (var context = database.CreateSupportDeskContext())
        {
            var entity = await context.Sessions.FindAsync(session.Id);
            entity!.TransferNote = "Khách cần đổi size, đơn PK098 đã giao.";
            await context.SaveChangesAsync();
        }

        await SupportConversationTestKit.SeedMessageAsync(
            database, conversation.Id, "Mình muốn đổi size áo.", handoffAt, SupportMessageRole.Customer);

        var request = ActorHttp.CreateRequest(
            HttpMethod.Get, "/api/admin/support/sessions?view=mine", $"pk_session={token}");
        var response = await _client.SendAsync(request);

        // So khớp cấu trúc dữ liệu của response với ví dụ phiên của tôi sau khi đồng nghiệp chuyển.
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(
            body, response.StatusCode, FixtureFileName, "phiên của tôi sau khi đồng nghiệp chuyển");
    }

    [Fact]
    // Hợp đồng nhận phiên thành công và nhận phiên đã có người nhận phải khớp ví dụ.
    public async Task Contract_ClaimSessionAndAlreadyClaimed_MatchesExamples()
    {
        var (staff1, token1) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-claim-1");
        var (_, token2) = await IdentityTestData.CreateStaffSessionAsync(
            database, "support", "contract-claim-2");

        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, Guid.NewGuid().ToString());
        var session = await SupportSessionTestKit.SeedSessionAsync(
            database, conversation, TestMoment.UtcDateTime.AddMinutes(-5), handoffInHours: true);

        // Gửi nhận phiên lần đầu tiên thành công trả 201 Created.
        var req1 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/assignment",
            body: null,
            $"pk_session={token1}");
        var res1 = await _client.SendAsync(req1);
        var body1 = JsonNode.Parse(await res1.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body1, res1.StatusCode, FixtureFileName, "nhận phiên");

        // Gửi nhận phiên lần thứ hai bởi nhân viên khác trả 409 Conflict đã có người nhận.
        var req2 = ActorHttp.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/admin/support/sessions/{session.Id}/assignment",
            body: null,
            $"pk_session={token2}");
        var res2 = await _client.SendAsync(req2);
        var body2 = JsonNode.Parse(await res2.Content.ReadAsStringAsync());
        SupportDeskContract.AssertMatches(body2, res2.StatusCode, FixtureFileName, "nhận phiên đã có người nhận");
    }
}
