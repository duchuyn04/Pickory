using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;

namespace Pickory.Modules.Catalog.Realtime;

// Tạo luồng SSE cho một kết nối. Luồng gồm sự kiện tồn kho và nhịp tim do chính kết nối này gửi.
internal sealed class StockEventStreamer(
    StockEventHub eventHub,
    IHostApplicationLifetime applicationLifetime,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);

    // Trình duyệt tự nối lại sau khi kết nối hết tuổi. Giới hạn tuổi giúp kết nối cũ không sống mãi khi tab bị treo.
    public static readonly TimeSpan MaxConnectionLifetime = TimeSpan.FromMinutes(30);

    public async IAsyncEnumerable<SseItem<object>> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Tạo timer trước khi đăng ký vào hub. Khi hub báo có kết nối thì timer đã chạy.
        using var heartbeatTimer = new PeriodicTimer(HeartbeatInterval, timeProvider);
        using var lifetimeLimit = new CancellationTokenSource(MaxConnectionLifetime, timeProvider);
        using var connection = eventHub.Register();
        // Một token chung cho vòng đọc và vòng nhịp tim. Request bị hủy, hết tuổi kết nối hoặc API đang tắt đều dừng cả hai vòng.
        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lifetimeLimit.Token, applicationLifetime.ApplicationStopping);
        var heartbeatLoop = RunHeartbeatLoopAsync(heartbeatTimer, connection, streamCancellation.Token);

        try
        {
            // Báo kết nối đã vào hub. Client và test biết sự kiện phát sau dòng này sẽ được nhận.
            yield return new SseItem<object>(
                new StockHeartbeatData(timeProvider.GetUtcNow().UtcDateTime), StockEventNames.Connected);
            while (await WaitForNextEventAsync(connection, streamCancellation.Token, cancellationToken))
            {
                while (connection.Reader.TryRead(out var stockEvent))
                {
                    // Tên sự kiện vào trường `event` của SSE. Client dùng tên này để chọn bộ nhận sự kiện.
                    yield return new SseItem<object>(stockEvent.Data, stockEvent.Name);
                }
            }
        }
        finally
        {
            // Chờ vòng nhịp tim dừng hẳn trước khi đóng kết nối.
            streamCancellation.Cancel();
            await heartbeatLoop;
        }
    }

    // Trả `false` khi luồng phải kết thúc bình thường: hết tuổi kết nối hoặc API đang tắt. Request bị hủy thì để `OperationCanceledException` thoát ra.
    private static async Task<bool> WaitForNextEventAsync(
        StockEventConnection connection, CancellationToken streamToken, CancellationToken requestToken)
    {
        try
        {
            return await connection.Reader.WaitToReadAsync(streamToken);
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            // Middleware lỗi chỉ bỏ qua `OperationCanceledException` khi client đã ngắt. Kết thúc êm để response đóng sạch.
            return false;
        }
    }

    private async Task RunHeartbeatLoopAsync(
        PeriodicTimer heartbeatTimer, StockEventConnection connection, CancellationToken streamToken)
    {
        try
        {
            while (await heartbeatTimer.WaitForNextTickAsync(streamToken))
            {
                connection.Send(new StockEvent(
                    StockEventNames.Heartbeat,
                    new StockHeartbeatData(timeProvider.GetUtcNow().UtcDateTime)));
            }
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
            // Luồng SSE kết thúc nên vòng nhịp tim bị hủy. Hủy do nguồn khác không nằm ở đây, nên không bị nuốt.
        }
    }
}
