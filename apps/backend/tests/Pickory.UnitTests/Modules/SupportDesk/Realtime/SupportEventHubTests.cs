using Pickory.Modules.SupportDesk.Realtime;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Realtime;

// SD-03: hub giữ một `Channel` cho mỗi kết nối SSE. Test chạy trong bộ nhớ, không cần DB hay HTTP.
public sealed class SupportEventHubTests
{
    private static SupportEvent NewEvent(string name = SupportEventNames.PresenceChanged) =>
        new(name, new { staffId = "staff-1", status = "available" });

    [Fact]
    public void Broadcast_TwoConnections_BothReceiveTheEvent()
    {
        var hub = new SupportEventHub();
        using var first = hub.Register("staff-1");
        using var second = hub.Register("staff-2");
        var supportEvent = NewEvent();

        hub.Broadcast(supportEvent);

        Assert.True(first.Reader.TryRead(out var firstReceived));
        Assert.True(second.Reader.TryRead(out var secondReceived));
        Assert.Same(supportEvent, firstReceived);
        Assert.Same(supportEvent, secondReceived);
    }

    [Fact]
    public void Register_TwoConnections_HubCountsBoth()
    {
        var hub = new SupportEventHub();

        using var first = hub.Register("staff-1");
        using var second = hub.Register("staff-1");

        Assert.Equal(2, hub.ConnectionCount);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Dispose_OneConnection_HubKeepsTheOtherAndStopsDeliveringToTheRemovedOne()
    {
        var hub = new SupportEventHub();
        var removed = hub.Register("staff-1");
        using var remaining = hub.Register("staff-2");

        removed.Dispose();
        hub.Broadcast(NewEvent());

        Assert.Equal(1, hub.ConnectionCount);
        Assert.False(removed.Reader.TryRead(out _));
        Assert.True(removed.Reader.Completion.IsCompleted);
        Assert.True(remaining.Reader.TryRead(out _));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrowAndKeepsTheCountCorrect()
    {
        var hub = new SupportEventHub();
        var connection = hub.Register("staff-1");
        using var other = hub.Register("staff-2");

        connection.Dispose();
        connection.Dispose();

        Assert.Equal(1, hub.ConnectionCount);
    }

    [Fact]
    public void Broadcast_NoConnection_DoesNothing()
    {
        var hub = new SupportEventHub();

        hub.Broadcast(NewEvent());

        Assert.Equal(0, hub.ConnectionCount);
    }

    [Fact]
    public void Broadcast_SlowClientOverTheLimit_KeepsTheNewestEventsAndDropsTheOldest()
    {
        var hub = new SupportEventHub();
        using var connection = hub.Register("staff-1");
        var total = SupportEventHub.ConnectionBufferSize + 5;

        for (var index = 0; index < total; index++)
        {
            hub.Broadcast(NewEvent($"event-{index}"));
        }

        var received = new List<string>();
        while (connection.Reader.TryRead(out var supportEvent))
        {
            received.Add(supportEvent.Name);
        }
        Assert.Equal(SupportEventHub.ConnectionBufferSize, received.Count);
        Assert.Equal("event-5", received[0]);
        Assert.Equal($"event-{total - 1}", received[^1]);
    }

    [Fact]
    public void Send_OneConnection_OnlyThatConnectionReceives()
    {
        var hub = new SupportEventHub();
        using var target = hub.Register("staff-1");
        using var other = hub.Register("staff-2");

        target.Send(NewEvent(SupportEventNames.Heartbeat));

        Assert.True(target.Reader.TryRead(out var received));
        Assert.Equal(SupportEventNames.Heartbeat, received.Name);
        Assert.False(other.Reader.TryRead(out _));
    }
}
