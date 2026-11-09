using System.Threading.Channels;

namespace Pickory.Modules.SupportDesk.Realtime;

// Một kết nối SSE phía khách đang mở. Đóng kết nối thì gỡ khỏi hub và kết thúc luồng đọc.
internal sealed class CustomerEventConnection : IDisposable
{
    private readonly CustomerEventHub _hub;
    private readonly Channel<CustomerStreamItem> _channel;
    private int _disposed;

    public CustomerEventConnection(Guid id, string conversationId, CustomerEventHub hub)
    {
        Id = id;
        ConversationId = conversationId;
        _hub = hub;
        // `DropOldest` bỏ dòng cũ nhất khi đầy. Khách chậm mất sự kiện cũ thay vì làm API giữ bộ nhớ mãi.
        _channel = Channel.CreateBounded<CustomerStreamItem>(
            new BoundedChannelOptions(CustomerEventHub.ConnectionBufferSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
    }

    public Guid Id { get; }

    // Hội thoại của kết nối. Hub dùng mã này để chỉ phát sự kiện cho đúng khách.
    public string ConversationId { get; }

    public ChannelReader<CustomerStreamItem> Reader => _channel.Reader;

    public void Send(CustomerStreamItem item)
    {
        // Kênh đã đóng thì `TryWrite` trả false. Kết quả bị bỏ qua vì kết nối này sắp rời hub.
        _channel.Writer.TryWrite(item);
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
        // Kết thúc luồng đọc để vòng ghi SSE đang chờ dòng mới dừng lại.
        _channel.Writer.TryComplete();
    }
}
