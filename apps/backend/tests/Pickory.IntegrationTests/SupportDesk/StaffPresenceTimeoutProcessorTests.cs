using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Modules.SupportDesk.Realtime;
using Xunit;

namespace Pickory.IntegrationTests.SupportDesk;

// SD-03: job chuyển nhân viên mất nhịp tim sang `away`. Test gọi thẳng processor với đồng hồ giả, không chờ worker.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class StaffPresenceTimeoutProcessorTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new(SupportDeskTestKit.NowAtMillisecondPrecision());
    private WebApplicationFactory<Program> _host = null!;
    private SupportEventHub _hub = null!;

    public async Task InitializeAsync()
    {
        await SupportDeskTestKit.ClearPresenceAsync(database);
        _host = SupportDeskTestKit.CreateHostWithClock(factory, _clock);
        _hub = _host.Services.GetRequiredService<SupportEventHub>();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await SupportDeskTestKit.ClearPresenceAsync(database);
    }

    [Fact]
    public async Task Process_AvailableStaffOlderThanTwoMinutes_BecomesAway()
    {
        var staffId = NewStaffId();
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, staffId, StaffPresenceStatus.Available, _clock.GetUtcNow() - TimeSpan.FromMinutes(3));

        var changedCount = await RunProcessorAsync();

        Assert.Equal(1, changedCount);
        Assert.Equal(StaffPresenceStatus.Away, await ReadStatusAsync(staffId));
    }

    [Fact]
    public async Task Process_LastSeenExactlyTwoMinutesAgo_StaysAvailable()
    {
        // Mốc là nửa mở: cũ đúng 2 phút còn `available`, cũ hơn 2 phút mới `away`.
        var staffId = NewStaffId();
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, staffId, StaffPresenceStatus.Available, _clock.GetUtcNow() - TimeSpan.FromMinutes(2));

        var changedCount = await RunProcessorAsync();

        Assert.Equal(0, changedCount);
        Assert.Equal(StaffPresenceStatus.Available, await ReadStatusAsync(staffId));
    }

    [Fact]
    public async Task Process_LastSeenOneMillisecondOlderThanTwoMinutes_BecomesAway()
    {
        var staffId = NewStaffId();
        await SupportDeskTestKit.SetPresenceRowAsync(
            database,
            staffId,
            StaffPresenceStatus.Available,
            _clock.GetUtcNow() - TimeSpan.FromMinutes(2) - TimeSpan.FromMilliseconds(1));

        var changedCount = await RunProcessorAsync();

        Assert.Equal(1, changedCount);
        Assert.Equal(StaffPresenceStatus.Away, await ReadStatusAsync(staffId));
    }

    [Fact]
    public async Task Process_RecentlySeenStaff_StaysAvailable()
    {
        var staffId = NewStaffId();
        await SupportDeskTestKit.SetPresenceRowAsync(
            database, staffId, StaffPresenceStatus.Available, _clock.GetUtcNow() - TimeSpan.FromSeconds(30));

        var changedCount = await RunProcessorAsync();

        Assert.Equal(0, changedCount);
        Assert.Equal(StaffPresenceStatus.Available, await ReadStatusAsync(staffId));
    }

    [Fact]
    public async Task Process_StaffAlreadyAway_IsNotTouchedAndNoEventIsSent()
    {
        var staffId = NewStaffId();
        var lastSeenAt = _clock.GetUtcNow() - TimeSpan.FromMinutes(10);
        await SupportDeskTestKit.SetPresenceRowAsync(database, staffId, StaffPresenceStatus.Away, lastSeenAt);
        using var listener = _hub.Register("listener");

        var changedCount = await RunProcessorAsync();

        Assert.Equal(0, changedCount);
        Assert.False(listener.Reader.TryRead(out _));
        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, staffId);
        Assert.NotNull(row);
        Assert.Equal(lastSeenAt.UtcDateTime, row.LastSeenAt);
    }

    [Fact]
    public async Task Process_SeveralStaleStaff_ChangesOnlyTheStaleOnes()
    {
        var staleOne = NewStaffId();
        var staleTwo = NewStaffId();
        var fresh = NewStaffId();
        await SupportDeskTestKit.SetPresenceRowAsync(database, staleOne, StaffPresenceStatus.Available, _clock.GetUtcNow() - TimeSpan.FromMinutes(5));
        await SupportDeskTestKit.SetPresenceRowAsync(database, staleTwo, StaffPresenceStatus.Available, _clock.GetUtcNow() - TimeSpan.FromMinutes(3));
        await SupportDeskTestKit.SetPresenceRowAsync(database, fresh, StaffPresenceStatus.Available, _clock.GetUtcNow());

        var changedCount = await RunProcessorAsync();

        Assert.Equal(2, changedCount);
        Assert.Equal(StaffPresenceStatus.Away, await ReadStatusAsync(staleOne));
        Assert.Equal(StaffPresenceStatus.Away, await ReadStatusAsync(staleTwo));
        Assert.Equal(StaffPresenceStatus.Available, await ReadStatusAsync(fresh));
    }

    [Fact]
    public async Task Process_StaleStaff_OtherOpenConnectionsReceivePresenceChangedForEachChangedStaff()
    {
        var staleOne = NewStaffId();
        var staleTwo = NewStaffId();
        await SupportDeskTestKit.SetPresenceRowAsync(database, staleOne, StaffPresenceStatus.Available, _clock.GetUtcNow() - TimeSpan.FromMinutes(5));
        await SupportDeskTestKit.SetPresenceRowAsync(database, staleTwo, StaffPresenceStatus.Available, _clock.GetUtcNow() - TimeSpan.FromMinutes(3));
        using var listener = _hub.Register("listener");

        await RunProcessorAsync();

        var received = new List<SupportEvent>();
        while (listener.Reader.TryRead(out var supportEvent))
        {
            received.Add(supportEvent);
        }
        Assert.Equal(2, received.Count);
        Assert.All(received, supportEvent => Assert.Equal(SupportEventNames.PresenceChanged, supportEvent.Name));
        var changed = received.Select(supportEvent => Assert.IsType<PresenceChangedEventData>(supportEvent.Data)).ToList();
        Assert.Equal(
            new[] { staleOne, staleTwo }.Order(StringComparer.Ordinal),
            changed.Select(data => data.StaffId).Order(StringComparer.Ordinal));
        Assert.All(changed, data => Assert.Equal("away", data.Status));
    }

    private static string NewStaffId() => $"staff-{Guid.NewGuid():N}";

    private async Task<int> RunProcessorAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<StaffPresenceTimeoutProcessor>();
        return await processor.MarkStaleStaffAwayAsync(CancellationToken.None);
    }

    private async Task<StaffPresenceStatus> ReadStatusAsync(string staffId)
    {
        var row = await SupportDeskTestKit.ReadPresenceRowAsync(database, staffId);
        Assert.NotNull(row);
        return row.Status;
    }
}
