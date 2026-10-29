using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pickory.Modules.SupportDesk.Presence;

namespace Pickory.Modules.SupportDesk.Realtime;

// Tạo luồng SSE cho một kết nối của nhân viên. Luồng gồm sự kiện từ hub và nhịp tim do chính kết nối này gửi.
internal sealed class SupportEventStreamer(
    SupportEventHub eventHub,
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime applicationLifetime,
    TimeProvider timeProvider,
    ILogger<SupportEventStreamer> logger)
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    // `RequirePermission` chỉ chạy lúc mở request. Giới hạn tuổi kết nối để tài khoản bị khóa, mất quyền hoặc đã đăng xuất không giữ luồng và nhịp tim mãi.
    public static readonly TimeSpan MaxConnectionLifetime = TimeSpan.FromMinutes(5);

    public async IAsyncEnumerable<SseItem<object>> StreamAsync(
        string staffId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Tạo hai timer trước khi đăng ký vào hub. Khi hub báo có kết nối thì timer đã chạy, nên đồng hồ giả của test tiến đúng lúc.
        using var heartbeatTimer = new PeriodicTimer(HeartbeatInterval, timeProvider);
        using var lifetimeLimit = new CancellationTokenSource(MaxConnectionLifetime, timeProvider);
        // `using` gỡ kết nối khỏi hub dù client ngắt, hết tuổi kết nối hay vòng gửi gặp lỗi.
        using var connection = eventHub.Register(staffId);
        // Một token chung cho vòng đọc và vòng nhịp tim. Request bị hủy, hết tuổi kết nối hoặc API đang tắt đều dừng cả hai vòng.
        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lifetimeLimit.Token, applicationLifetime.ApplicationStopping);
        var heartbeatLoop = RunHeartbeatLoopAsync(heartbeatTimer, connection, streamCancellation.Token);

        try
        {
            while (await WaitForNextEventAsync(connection, streamCancellation.Token, cancellationToken))
            {
                while (connection.Reader.TryRead(out var supportEvent))
                {
                    // Tên sự kiện vào trường `event` của SSE. Client dùng tên này để chọn bộ nhận sự kiện.
                    yield return new SseItem<object>(supportEvent.Data, supportEvent.Name);
                }
            }
        }
        finally
        {
            // Chờ vòng nhịp tim dừng hẳn. Vòng này không còn ghi DB sau khi kết nối đã đóng.
            streamCancellation.Cancel();
            await heartbeatLoop;
        }
    }

    // Trả `false` khi luồng phải kết thúc bình thường: hết tuổi kết nối hoặc API đang tắt. Request bị hủy thì để `OperationCanceledException` thoát ra.
    private static async Task<bool> WaitForNextEventAsync(
        SupportEventConnection connection, CancellationToken streamToken, CancellationToken requestToken)
    {
        try
        {
            return await connection.Reader.WaitToReadAsync(streamToken);
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            // Middleware lỗi chỉ bỏ qua `OperationCanceledException` khi client đã ngắt. Kết thúc êm để response đóng sạch và `EventSource` tự nối lại.
            return false;
        }
    }

    private async Task RunHeartbeatLoopAsync(
        PeriodicTimer heartbeatTimer, SupportEventConnection connection, CancellationToken streamToken)
    {
        try
        {
            while (await heartbeatTimer.WaitForNextTickAsync(streamToken))
            {
                await RecordHeartbeatAsync(connection.StaffId, streamToken);
                // Ghi DB trước rồi mới gửi. Ghi lỗi thì vẫn gửi để giữ kết nối, nên nhịp tim không chứng minh DB đã ghi.
                connection.Send(new SupportEvent(
                    SupportEventNames.Heartbeat,
                    new HeartbeatEventData(timeProvider.GetUtcNow().UtcDateTime)));
            }
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
            // Luồng SSE kết thúc nên vòng nhịp tim bị hủy. Hủy do nguồn khác không nằm ở đây, nên không bị nuốt.
        }
    }

    private async Task RecordHeartbeatAsync(string staffId, CancellationToken cancellationToken)
    {
        try
        {
            // Mỗi nhịp dùng scope mới để `DbContext` không giữ dữ liệu cũ giữa các nhịp.
            await using var scope = scopeFactory.CreateAsyncScope();
            var presenceService = scope.ServiceProvider.GetRequiredService<StaffPresenceService>();
            await presenceService.RecordHeartbeatAsync(staffId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Lỗi DB tạm thời không được đóng luồng SSE. Nhịp sau sẽ thử lại, và job `away` chỉ chạy khi mất nhịp quá 2 phút.
            logger.LogError(exception, "Không làm mới được `lastSeenAt` cho nhân viên {StaffId}.", staffId);
        }
    }
}
