using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-15: đăng nhập phát `UserLinkedEvent`, và `SupportConversationLinkHandler` gắn hội thoại vãng lai vào tài khoản.
// Test đăng nhập bằng route thật, nên cả đường phát sự kiện lẫn handler cùng được kiểm.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class SupportConversationLinkHandlerTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public SupportConversationLinkHandlerTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Bộ đếm đăng nhập sai là singleton dùng chung. Xóa bộ đếm để test trước không khóa test này.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => SupportConversationTestKit.DeleteIssuedConversationsAsync(_database);

    [Fact]
    public async Task Login_GuestWithConversationAndMessages_KeepsSameConversationAndHistoryForTheAccount()
    {
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var user = await CreateCustomerAsync();

        // Khách vãng lai mở hội thoại và gửi tin qua route thật, như trình duyệt.
        var opened = await SupportConversationTestKit.ReadDataAsync(await _client.SendAsync(
            SupportConversationTestKit.Post(SupportConversationTestKit.ConversationsPath, guestCookie)));
        var conversationId = opened.GetProperty("id").GetString()!;
        var sent = await _client.SendAsync(SupportConversationTestKit.Post(
            SupportConversationTestKit.MessagesPath(conversationId), guestCookie, new { body = "Tôi cần hỗ trợ" }));
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        // Cùng trình duyệt đó đăng nhập, nên request mang cookie `pk_anon` cũ.
        var sessionCookie = await LoginAsync(user, guestCookie);

        var reopened = await _client.SendAsync(SupportConversationTestKit.Post(
            SupportConversationTestKit.ConversationsPath, $"{sessionCookie}; {guestCookie}"));
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var reopenedData = await SupportConversationTestKit.ReadDataAsync(reopened);
        Assert.Equal(conversationId, reopenedData.GetProperty("id").GetString());
        Assert.False(reopenedData.GetProperty("showRetentionNotice").GetBoolean());

        var history = await SupportConversationTestKit.ReadDataAsync(await _client.SendAsync(SupportConversationTestKit.Get(
            SupportConversationTestKit.MessagesPath(conversationId), $"{sessionCookie}; {guestCookie}")));
        Assert.Equal("Tôi cần hỗ trợ", Assert.Single(history.GetProperty("items").EnumerateArray()).GetProperty("body").GetString());
        Assert.Equal(user.Id, (await SupportConversationTestKit.ReadConversationAsync(_database, conversationId)).CustomerUserId);
    }

    [Fact]
    public async Task Login_AccountBOnBrowserWithConversationOfAccountA_DoesNotTakeOverConversation()
    {
        var sharedCookie = SupportConversationTestKit.NewGuestCookie();
        var accountA = await CreateCustomerAsync();
        var accountB = await CreateCustomerAsync();
        var conversationOfA = await SupportConversationTestKit.SeedConversationAsync(
            _database, SupportConversationTestKit.ReadAnonymousId(sharedCookie), customerUserId: accountA.Id);

        await LoginAsync(accountB, sharedCookie);

        // Hội thoại của A giữ nguyên chủ và trạng thái.
        var after = await SupportConversationTestKit.ReadConversationAsync(_database, conversationOfA.Id);
        Assert.Equal(accountA.Id, after.CustomerUserId);
        Assert.Equal(SupportConversationStatus.Open, after.Status);
        Assert.Null(after.ClosedAt);
    }

    [Fact]
    public async Task Login_AccountAlreadyHasNewerOpenConversation_ClosesTheOlderAccountOneAndLinksGuestOne()
    {
        var user = await CreateCustomerAsync();
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        // Hội thoại của tài khoản im lặng lâu hơn hội thoại vãng lai.
        var accountConversation = await SupportConversationTestKit.SeedConversationAsync(
            _database, Guid.NewGuid().ToString(), user.Id, lastMessageAt: DateTime.UtcNow.AddHours(-5));
        var guestConversation = await SupportConversationTestKit.SeedConversationAsync(
            _database, SupportConversationTestKit.ReadAnonymousId(guestCookie), lastMessageAt: DateTime.UtcNow.AddHours(-1));

        await LoginAsync(user, guestCookie);

        var accountAfter = await SupportConversationTestKit.ReadConversationAsync(_database, accountConversation.Id);
        Assert.Equal(SupportConversationStatus.Closed, accountAfter.Status);
        Assert.NotNull(accountAfter.ClosedAt);
        var guestAfter = await SupportConversationTestKit.ReadConversationAsync(_database, guestConversation.Id);
        Assert.Equal(SupportConversationStatus.Open, guestAfter.Status);
        Assert.Equal(user.Id, guestAfter.CustomerUserId);
    }

    [Fact]
    public async Task Login_AccountAlreadyHasMoreRecentOpenConversation_ClosesTheOlderGuestOneAndKeepsAccountOne()
    {
        var user = await CreateCustomerAsync();
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var accountConversation = await SupportConversationTestKit.SeedConversationAsync(
            _database, Guid.NewGuid().ToString(), user.Id, lastMessageAt: DateTime.UtcNow.AddHours(-1));
        var guestConversation = await SupportConversationTestKit.SeedConversationAsync(
            _database, SupportConversationTestKit.ReadAnonymousId(guestCookie), lastMessageAt: DateTime.UtcNow.AddHours(-5));

        await LoginAsync(user, guestCookie);

        var accountAfter = await SupportConversationTestKit.ReadConversationAsync(_database, accountConversation.Id);
        Assert.Equal(SupportConversationStatus.Open, accountAfter.Status);
        var guestAfter = await SupportConversationTestKit.ReadConversationAsync(_database, guestConversation.Id);
        Assert.Equal(SupportConversationStatus.Closed, guestAfter.Status);
        Assert.NotNull(guestAfter.ClosedAt);
        // Hội thoại đã đóng vẫn thuộc về tài khoản để khách đọc lại lịch sử.
        Assert.Equal(user.Id, guestAfter.CustomerUserId);
    }

    [Fact]
    public async Task Login_GuestWithoutConversation_CreatesNothing()
    {
        var user = await CreateCustomerAsync();
        var guestCookie = SupportConversationTestKit.NewGuestCookie();

        await LoginAsync(user, guestCookie);

        Assert.Empty(await SupportConversationTestKit.ReadConversationsAsync(
            _database, SupportConversationTestKit.ReadAnonymousId(guestCookie)));
    }

    [Fact]
    public async Task Login_GuestWithOnlyClosedConversation_LeavesItUnlinked()
    {
        var user = await CreateCustomerAsync();
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var closed = await SupportConversationTestKit.SeedConversationAsync(
            _database, SupportConversationTestKit.ReadAnonymousId(guestCookie), status: SupportConversationStatus.Closed);

        await LoginAsync(user, guestCookie);

        // Hội thoại đã đóng không còn hoạt động, nên không gắn vào tài khoản.
        Assert.Null((await SupportConversationTestKit.ReadConversationAsync(_database, closed.Id)).CustomerUserId);
    }

    [Fact]
    public async Task Handle_AccountConversationAppearsBetweenReadAndSave_RetriesAndLinksWithoutLosingTheGuestConversation()
    {
        var user = await CreateCustomerAsync();
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var guestConversation = await SupportConversationTestKit.SeedConversationAsync(
            _database, SupportConversationTestKit.ReadAnonymousId(guestCookie), lastMessageAt: DateTime.UtcNow.AddHours(-5));
        // Ngay sau khi handler đọc "tài khoản chưa có hội thoại `open`", một tab khác của tài khoản tạo hội thoại `open` mới hơn.
        var injectedAnonymousId = Guid.NewGuid().ToString();
        var interceptor = new InsertAccountConversationAfterAccountLookup(_database, user.Id, injectedAnonymousId);
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.ConfigureDbContext<SupportDeskDbContext>(options => options.AddInterceptors(interceptor))));
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IDomainEventHandler<UserLinkedEvent>>()
            .OfType<SupportConversationLinkHandler>().Single();

        await handler.HandleAsync(new UserLinkedEvent(SupportConversationTestKit.ReadAnonymousId(guestCookie), user.Id));

        // Lần thử đầu gặp `23505`, nên interceptor đã chèn hội thoại xung đột đúng một lần.
        Assert.True(interceptor.Inserted);
        // Lần thử thứ hai thấy hội thoại mới hơn của tài khoản. Nó đóng hội thoại vãng lai cũ hơn, rồi vẫn gắn hội thoại đó vào tài khoản.
        var guestAfter = await SupportConversationTestKit.ReadConversationAsync(_database, guestConversation.Id);
        Assert.Equal(user.Id, guestAfter.CustomerUserId);
        Assert.Equal(SupportConversationStatus.Closed, guestAfter.Status);
        var accountConversations = await ReadConversationsOfAccountAsync(user.Id);
        Assert.Single(accountConversations, conversation => conversation.Status == SupportConversationStatus.Open);
        Assert.Equal(2, accountConversations.Count);
    }

    private async Task<IReadOnlyList<SupportConversation>> ReadConversationsOfAccountAsync(string userId)
    {
        await using var context = _database.CreateSupportDeskContext();
        return await context.Conversations.AsNoTracking().Where(conversation => conversation.CustomerUserId == userId).ToListAsync();
    }

    // Sau lần đọc hội thoại `open` của tài khoản, chèn một hội thoại `open` của tài khoản đó bằng kết nối khác. Chỉ chèn một lần.
    private sealed class InsertAccountConversationAfterAccountLookup(
        IntegrationDatabaseFixture database, string userId, string anonymousId) : DbCommandInterceptor
    {
        private int _inserted;

        public bool Inserted => _inserted == 1;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            // Câu tìm hội thoại của tài khoản lọc theo `customerUserId` và không lọc theo `anonymousId` hay `id`.
            var isAccountLookup = command.CommandText.Contains("FROM support_conversations", StringComparison.Ordinal)
                && command.CommandText.Contains("\"customerUserId\" =", StringComparison.Ordinal)
                && !command.CommandText.Contains("\"anonymousId\" =", StringComparison.Ordinal)
                && !command.CommandText.Contains(".id = ", StringComparison.Ordinal);
            if (isAccountLookup && Interlocked.Exchange(ref _inserted, 1) == 0)
            {
                await SupportConversationTestKit.SeedConversationAsync(
                    database, anonymousId, userId, lastMessageAt: DateTime.UtcNow);
            }

            return result;
        }
    }

    private Task<User> CreateCustomerAsync()
    {
        return IdentityTestData.CreateUserAsync(
            _database, IdentityTestData.NewEmail("support-link"), UserKind.Customer, "customer");
    }

    // Đăng nhập bằng route thật và trả cookie phiên. `UserLinkedEvent` phát trong request này.
    private async Task<string> LoginAsync(User user, string anonymousCookie)
    {
        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/sessions", new { email = user.Email, password = IdentityTestData.DefaultPassword }, anonymousCookie));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return $"pk_session={ActorHttp.ReadSessionToken(response)}";
    }
}
