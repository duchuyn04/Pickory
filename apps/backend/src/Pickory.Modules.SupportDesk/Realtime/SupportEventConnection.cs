using System.Threading.Channels;

namespace Pickory.Modules.SupportDesk.Realtime;

// Một kết nối SSE đang mở. Đóng kết nối thì gỡ khỏi hub và kết thúc luồng đọc.
internal sealed class SupportEventConnection : IDisposable
{
    private readonly SupportEventHub _hub;
    private readonly Channel<SupportEvent> _channel;
    private int _disposed;

    public SupportEventConnection(Guid id, string staffId, SupportEventHub hub)
    {
        Id = id;
        StaffId = staffId;
        _hub = hub;
        // `DropOldest` bỏ sự kiện cũ nhất khi đầy. Nhân viên chậm mất sự kiện cũ thay vì làm API giữ bộ nhớ mãi.
        _channel = Channel.CreateBounded<SupportEvent>(new BoundedChannelOptions(SupportEventHub.ConnectionBufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    public Guid Id { get; }

    public string StaffId { get; }

    public ChannelReader<SupportEvent> Reader => _channel.Reader;

    public void Send(SupportEvent supportEvent)
    {
        // Kênh đã đóng thì `TryWrite` trả false. Kết quả bị bỏ qua vì kết nối này sắp rời hub.
        _channel.Writer.TryWrite(supportEvent);
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
