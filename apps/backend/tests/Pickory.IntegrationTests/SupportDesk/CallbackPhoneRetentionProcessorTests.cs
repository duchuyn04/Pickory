using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Callbacks;
using Pickory.Modules.SupportDesk.Entities;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-05: job hằng ngày xóa số điện thoại và ghi chú cũ hơn 30 ngày, giữ lại dòng cho báo cáo.
// Test gọi thẳng `CallbackPhoneRetentionProcessor` với `FakeTimeProvider` và gỡ worker nền để không chạy tranh chấp lúc khởi động.
[Collection(SupportDeskCollection.Name)]
public sealed class CallbackPhoneRetentionProcessorTests(
    SupportDeskWebApplicationFactory factory,
    SupportDeskDatabaseFixture database)
    : IClassFixture<SupportDeskWebApplicationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset TestMoment = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(TestMoment);
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        var cutoff = TestMoment.AddDays(-30).UtcDateTime;
        await using var context = database.CreateSupportDeskContext();
        // Xóa dòng quá hạn của test khác để bộ đếm số dòng làm sạch không bị lệch.
        await context.CallbackRequests
            .Where(request => request.CreatedAt < cutoff)
            .ExecuteDeleteAsync();

        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock, services =>
        {
            // Gỡ worker tự động để vòng khởi động của host không xóa dòng trước lúc test gọi processor.
            var worker = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(CallbackPhoneRetentionWorker));
            if (worker is not null)
            {
                services.Remove(worker);
            }
        });
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await SupportConversationTestKit.DeleteIssuedConversationsAsync(database);
        await CallbackRequestTestKit.DeleteIssuedCallbackRequestsAsync(database);
    }

    [Fact]
    // Yêu cầu 30 ngày 1 phút tuổi mất cả `Phone` và `Note`, nhưng giữ nguyên dòng cùng các cột số liệu.
    public async Task Redact_OlderThan30Days_ClearsPhoneAndNoteAndKeepsRowForMetrics()
    {
        var guestCookie = SupportConversationTestKit.NewGuestCookie();
        var anonymousId = SupportConversationTestKit.ReadAnonymousId(guestCookie);
        var conversation = await SupportConversationTestKit.SeedConversationAsync(database, anonymousId);
        var createdAt = TestMoment.AddDays(-30).AddMinutes(-1).UtcDateTime;
        var handledAt = TestMoment.AddDays(-29).UtcDateTime;
        var seeded = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            anonymousId,
            conversationId: conversation.Id,
            phone: "0901234567",
            note: "Gọi sau 18 giờ.",
            status: CallbackRequestStatus.Done,
            createdAt: createdAt,
            handledBy: "staff-1",
            handledAt: handledAt);

        var redactedCount = await RunProcessorAsync();
        var secondRunCount = await RunProcessorAsync();

        Assert.Equal(1, redactedCount);
        // Lần chạy thứ hai bỏ qua dòng đã xóa số để bộ đếm trả đúng số dòng vừa làm sạch.
        Assert.Equal(0, secondRunCount);
        var stored = await CallbackRequestTestKit.FindCallbackRequestAsync(database, seeded.Id);
        Assert.NotNull(stored);
        Assert.Null(stored!.Phone);
        Assert.Null(stored.Note);
        Assert.Equal(CallbackRequestStatus.Done, stored.Status);
        Assert.Equal(createdAt, stored.CreatedAt);
        Assert.Equal("staff-1", stored.HandledBy);
        Assert.Equal(handledAt, stored.HandledAt);
        Assert.Equal(conversation.Id, stored.ConversationId);
    }

    [Fact]
    // Điều kiện là cũ hơn 30 ngày (`<`), nên dòng đúng tròn 30 ngày và dòng 29 ngày 23 giờ vẫn giữ nguyên.
    public async Task Redact_AtOrUnder30DaysOld_KeepsPhoneAndNote()
    {
        var anonymousId = Guid.NewGuid().ToString();
        CallbackRequestTestKit.TrackAnonymousId(anonymousId);
        var exactBoundary = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            anonymousId,
            phone: "0901234567",
            note: "Đúng tròn 30 ngày.",
            createdAt: TestMoment.AddDays(-30).UtcDateTime);
        var underBoundary = await CallbackRequestTestKit.SeedCallbackRequestAsync(
            database,
            anonymousId,
            phone: "0909876543",
            note: "Còn một giờ nữa tròn 30 ngày.",
            createdAt: (TestMoment - TimeSpan.FromDays(29) - TimeSpan.FromHours(23)).UtcDateTime);

        var redactedCount = await RunProcessorAsync();

        Assert.Equal(0, redactedCount);
        var exactAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, exactBoundary.Id);
        Assert.Equal("0901234567", exactAfter!.Phone);
        Assert.Equal("Đúng tròn 30 ngày.", exactAfter.Note);
        var underAfter = await CallbackRequestTestKit.FindCallbackRequestAsync(database, underBoundary.Id);
        Assert.Equal("0909876543", underAfter!.Phone);
        Assert.Equal("Còn một giờ nữa tròn 30 ngày.", underAfter.Note);
    }

    private async Task<int> RunProcessorAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<CallbackPhoneRetentionProcessor>();
        return await processor.RedactExpiredPhonesAsync(CancellationToken.None);
    }
}
