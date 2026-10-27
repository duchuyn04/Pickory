using System.Threading.Channels;

namespace Pickory.Modules.Catalog.Realtime;

// Một kết nối SSE đang mở. Đóng kết nối thì gỡ khỏi hub và kết thúc luồng đọc.
internal sealed class StockEventConnection : IDisposable
{
    private readonly StockEventHub _hub;
    private readonly Channel<StockEvent> _channel;
    private int _disposed;

    public StockEventConnection(Guid id, StockEventHub hub)
    {
        Id = id;
        _hub = hub;
        // Kết nối chậm bỏ sự kiện cũ nhất thay vì làm API giữ bộ nhớ vô hạn.
        _channel = Channel.CreateBounded<StockEvent>(new BoundedChannelOptions(StockEventHub.ConnectionBufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    public Guid Id { get; }

    public ChannelReader<StockEvent> Reader => _channel.Reader;

    public void Send(StockEvent stockEvent)
    {
        // Kênh đã đóng thì `TryWrite` trả false. Bỏ qua vì kết nối này sắp rời hub.
        _channel.Writer.TryWrite(stockEvent);
    }

    public void Dispose()
    {
        // Chỉ lần gọi đầu tiên chạy tiếp. Gọi `Dispose` hai lần không gỡ nhầm kết nối khác.
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Gỡ khỏi hub trước để lần phát kế tiếp không tìm thấy kết nối này.
        _hub.Remove(Id);
        // Kết thúc luồng đọc để vòng gửi SSE đang chờ sự kiện dừng lại.
        _channel.Writer.TryComplete();
    }
}
