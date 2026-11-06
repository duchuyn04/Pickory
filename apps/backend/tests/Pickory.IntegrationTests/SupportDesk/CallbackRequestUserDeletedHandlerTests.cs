using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-05: khi nhận `UserDeletedEvent`, module xóa số điện thoại và ghi chú trên mọi yêu cầu gọi lại của người dùng đó.
[Collection(SupportDeskCollection.Name)]
public sealed class CallbackRequestUserDeletedHandlerTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTime CreatedAt = new(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
        await CallbackRequestTestKit.DeleteIssuedCallbackRequestsAsync(database);
    }

    [Fact]
    // Xóa số điện thoại cho cả dòng gắn thẳng `CustomerUserId` lẫn dòng gửi lúc vãng lai trong hội thoại đã gắn tài khoản sau đó.
    public async Task Dispatch_UserDeleted_ClearsPhoneAndNoteForDirectAndLinkedConversationRowsOnly()
    {
        var deletedUserId = $"user-{Guid.NewGuid():N}";
        var otherUserId = $"user-{Guid.NewGuid():N}";

        var directAnonymousId = Guid.NewGuid().ToString();
        var directRow = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            directAnonymousId,
            customerUserId: deletedUserId,
            phone: "0901111111",
            note: "Ghi chú của tài khoản.",
            status: CallbackRequestStatus.Done,
            createdAt: CreatedAt,
            handledBy: "staff-1",
            handledAt: CreatedAt.AddMinutes(10));

        // Khách gửi yêu cầu lúc chưa đăng nhập nên `CustomerUserId` trên dòng gọi lại rỗng, rồi đăng nhập gắn chủ cho hội thoại.
        var linkedGuestCookie = SupportConversationTestKit.NewGuestCookie();
        var linkedAnonymousId = SupportConversationTestKit.ReadAnonymousId(linkedGuestCookie);
        var linkedConversation = await SupportConversationTestKit.SeedConversationAsync(
            database, linkedAnonymousId, customerUserId: deletedUserId);
        var linkedConversationRow = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            linkedAnonymousId,
            conversationId: linkedConversation.Id,
            customerUserId: null,
            phone: "0902222222",
            note: "Ghi chú lúc chưa đăng nhập.",
            createdAt: CreatedAt);

        var otherCookie = SupportConversationTestKit.NewGuestCookie();
        var otherAnonymousId = SupportConversationTestKit.ReadAnonymousId(otherCookie);
        var otherConversation = await SupportConversationTestKit.SeedConversationAsync(
            database, otherAnonymousId, customerUserId: otherUserId);
        var otherUserRow = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            otherAnonymousId,
            conversationId: otherConversation.Id,
            customerUserId: otherUserId,
            phone: "0903333333",
            note: "Ghi chú của người khác.",
            createdAt: CreatedAt);

        var guestOnlyCookie = SupportConversationTestKit.NewGuestCookie();
        var guestOnlyAnonymousId = SupportConversationTestKit.ReadAnonymousId(guestOnlyCookie);
        var guestOnlyConversation = await SupportConversationTestKit.SeedConversationAsync(database, guestOnlyAnonymousId);
        var guestOnlyRow = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            guestOnlyAnonymousId,
            conversationId: guestOnlyConversation.Id,
            customerUserId: null,
            phone: "0904444444",
            note: "Ghi chú khách vãng lai.",
            createdAt: CreatedAt);

        await DispatchUserDeletedAsync(deletedUserId);

        var directAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, directRow.Id);
        Assert.NotNull(directAfter);
        Assert.Null(directAfter!.Phone);
        Assert.Null(directAfter.Note);
        Assert.Equal(CallbackRequestStatus.Done, directAfter.Status);
        Assert.Equal(CreatedAt.AddMinutes(10), directAfter.HandledAt);

        var linkedAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, linkedConversationRow.Id);
        Assert.NotNull(linkedAfter);
        Assert.Null(linkedAfter!.Phone);
        Assert.Null(linkedAfter.Note);
        // Xóa hội thoại thuộc SD-17, nên hội thoại vẫn còn nguyên ở bước này.
        Assert.NotNull(await SupportConversationTestKit.ReadConversationAsync(database, linkedConversation.Id));

        var otherAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, otherUserRow.Id);
        Assert.Equal("0903333333", otherAfter!.Phone);
        Assert.Equal("Ghi chú của người khác.", otherAfter.Note);

        var guestAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, guestOnlyRow.Id);
        Assert.Equal("0904444444", guestAfter!.Phone);
        Assert.Equal("Ghi chú khách vãng lai.", guestAfter.Note);
    }

    private async Task DispatchUserDeletedAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
        await dispatcher.PublishAsync(new UserDeletedEvent(userId), CancellationToken.None);
    }
}
