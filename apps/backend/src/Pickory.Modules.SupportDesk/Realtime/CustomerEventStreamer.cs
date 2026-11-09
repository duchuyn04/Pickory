using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;

namespace Pickory.Modules.SupportDesk.Realtime;

// Tạo luồng SSE cho một hội thoại. Luồng gồm sự kiện từ hub và dòng chú thích do chính kết nối này gửi.
internal sealed class CustomerEventStreamer(
    CustomerEventHub eventHub,
    IHostApplicationLifetime applicationLifetime,
    TimeProvider timeProvider)
{
    // Chu kỳ chú thích giữ kết nối. Proxy và trình duyệt đóng kết nối rảnh lâu hơn khoảng này.
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    public async IAsyncEnumerable<CustomerStreamItem> StreamAsync(
        string conversationId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Tạo timer trước khi đăng ký vào hub. Khi hub báo có kết nối thì timer đã chạy, nên đồng hồ giả của test tiến đúng lúc.
        using var keepAliveTimer = new PeriodicTimer(KeepAliveInterval, timeProvider);
        // `using` gỡ kết nối khỏi hub dù client ngắt hay API đang tắt.
        using var connection = eventHub.Register(conversationId);
        // Một token chung cho vòng đọc và vòng chú thích. Request bị hủy hoặc API đang tắt đều dừng cả hai vòng.
        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, applicationLifetime.ApplicationStopping);
        var keepAliveLoop = RunKeepAliveLoopAsync(keepAliveTimer, connection, streamCancellation.Token);

        try
        {
            while (await WaitForNextItemAsync(connection, streamCancellation.Token, cancellationToken))
            {
                while (connection.Reader.TryRead(out var item))
                {
                    // Tên sự kiện vào trường `event` của SSE. Chú thích giữ kết nối đi qua cùng hàng đợi.
                    yield return item;
                }
            }
        }
        finally
        {
            // Chờ vòng chú thích dừng hẳn. Vòng này không còn ghi vào kết nối đã đóng.
            streamCancellation.Cancel();
            await keepAliveLoop;
        }
    }

    // Trả `false` khi luồng phải kết thúc bình thường: API đang tắt. Request bị hủy thì để `OperationCanceledException` thoát ra.
    private static async Task<bool> WaitForNextItemAsync(
        CustomerEventConnection connection, CancellationToken streamToken, CancellationToken requestToken)
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

    private async Task RunKeepAliveLoopAsync(
        PeriodicTimer keepAliveTimer, CustomerEventConnection connection, CancellationToken streamToken)
    {
        try
        {
            while (await keepAliveTimer.WaitForNextTickAsync(streamToken))
            {
                // Ghi một dòng chú thích vào đúng kết nối này. Khách không nhận nó như một sự kiện.
                connection.Send(CustomerStreamItem.KeepAliveComment.Instance);
            }
        }
        catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
        {
            // Luồng SSE kết thúc nên vòng chú thích bị hủy. Hủy do nguồn khác không nằm ở đây, nên không bị nuốt.
        }
    }
}
