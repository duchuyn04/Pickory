using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-15: tài khoản A chat rồi đăng xuất, tài khoản B đăng nhập trên cùng trình duyệt (cùng `pk_anon`).
// Hội thoại của A vẫn mang `anonymousId` của trình duyệt đó, nhưng B không được thấy nó.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class SharedBrowserOwnershipTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => SupportConversationTestKit.DeleteIssuedConversationsAsync(database);

    [Fact]
    public async Task Get_AccountBOnBrowserOfAccountA_Returns404()
    {
        var (conversation, accountBCookie) = await SeedConversationOfAccountAAsync();

        var response = await _client.SendAsync(SupportConversationTestKit.Get(
            SupportConversationTestKit.MessagesPath(conversation.Id), accountBCookie));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
    }

    [Fact]
    public async Task PostMessage_AccountBOnBrowserOfAccountA_Returns404AndStoresNothing()
    {
        var (conversation, accountBCookie) = await SeedConversationOfAccountAAsync();

        var response = await _client.SendAsync(SupportConversationTestKit.Post(
            SupportConversationTestKit.MessagesPath(conversation.Id), accountBCookie, new { body = "Xin chào" }));

        await SupportConversationTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "SUPPORT_CONVERSATION_NOT_FOUND");
        Assert.Empty(await SupportConversationTestKit.ReadMessagesAsync(database, conversation.Id));
    }

    [Fact]
    public async Task PostConversation_AccountBOnBrowserOfAccountA_CreatesOwnConversation()
    {
        var (conversationOfA, accountBCookie) = await SeedConversationOfAccountAAsync();

        var response = await _client.SendAsync(SupportConversationTestKit.Post(
            SupportConversationTestKit.ConversationsPath, accountBCookie));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await SupportConversationTestKit.ReadDataAsync(response);
        Assert.NotEqual(conversationOfA.Id, data.GetProperty("id").GetString());
        // Hội thoại của A giữ nguyên chủ và trạng thái.
        var after = await SupportConversationTestKit.ReadConversationAsync(database, conversationOfA.Id);
        Assert.Equal(conversationOfA.CustomerUserId, after.CustomerUserId);
        Assert.Equal(SupportConversationStatus.Open, after.Status);
    }

    // Hội thoại `open` của A mang `anonymousId` X. B đăng nhập với cookie phiên của B kèm `pk_anon` X.
    private async Task<(SupportConversation ConversationOfA, string AccountBCookie)> SeedConversationOfAccountAAsync()
    {
        var sharedBrowserCookie = SupportConversationTestKit.NewGuestCookie();
        var (accountA, _) = await SupportConversationTestKit.CreateCustomerAsync(database);
        var conversationOfA = await SupportConversationTestKit.SeedConversationAsync(
            database, SupportConversationTestKit.ReadAnonymousId(sharedBrowserCookie), customerUserId: accountA.Id);
        var (_, accountBCookie) = await SupportConversationTestKit.CreateCustomerAsync(database, sharedBrowserCookie);
        return (conversationOfA, accountBCookie);
    }
}
