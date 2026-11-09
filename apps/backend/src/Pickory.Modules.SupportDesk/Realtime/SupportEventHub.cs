using System.Collections.Concurrent;

namespace Pickory.Modules.SupportDesk.Realtime;

// Giữ các kết nối SSE của nhân viên trong bộ nhớ tiến trình. MVP chạy một instance nên không cần bus giữa nhiều tiến trình.
internal sealed class SupportEventHub
{
    // Giới hạn số sự kiện chờ của mỗi kết nối. Client chậm không làm đầy bộ nhớ của API.
    public const int ConnectionBufferSize = 100;

    private readonly ConcurrentDictionary<Guid, SupportEventConnection> _connections = new();

    public int ConnectionCount => _connections.Count;

    public SupportEventConnection Register(string staffId)
    {
        // Mỗi kết nối có mã riêng. Một nhân viên mở nhiều tab vẫn có nhiều kết nối độc lập.
        var connection = new SupportEventConnection(Guid.NewGuid(), staffId, this);
        _connections[connection.Id] = connection;
        return connection;
    }

    public void Broadcast(SupportEvent supportEvent)
    {
        // Duyệt bản chụp của từ điển. Kết nối đóng giữa lúc phát không làm hỏng vòng lặp.
        foreach (var connection in _connections.Values)
        {
            connection.Send(supportEvent);
        }
    }

    // Chỉ gửi tới kết nối của các nhân viên được chọn. Sự kiện phiên chờ dùng hàm này để nhân viên tạm vắng không nhận.
    public void BroadcastToStaff(IReadOnlyCollection<string> staffIds, SupportEvent supportEvent)
    {
        // Đổi sang tập mã một lần. Vòng dưới so với từng kết nối, và danh sách nhân viên có thể dài.
        var receivers = new HashSet<string>(staffIds, StringComparer.Ordinal);
        // Duyệt bản chụp của từ điển, giống `Broadcast`.
        foreach (var connection in _connections.Values)
        {
            // Kết nối của nhân viên ngoài danh sách không nhận gì.
            if (!receivers.Contains(connection.StaffId))
            {
                continue;
            }

            connection.Send(supportEvent);
        }
    }

    // Chỉ `SupportEventConnection` gọi hàm này khi đóng, để hub không giữ kết nối đã đóng.
    internal void Remove(Guid connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }
}
