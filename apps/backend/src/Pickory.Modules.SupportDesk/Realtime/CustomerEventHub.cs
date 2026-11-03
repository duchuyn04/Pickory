using System.Collections.Concurrent;

namespace Pickory.Modules.SupportDesk.Realtime;

// Giữ các kết nối SSE phía khách trong bộ nhớ tiến trình. MVP chạy một instance nên không cần bus giữa nhiều tiến trình.
internal sealed class CustomerEventHub
{
    // Giới hạn số dòng chờ của mỗi kết nối. Khách chậm không làm đầy bộ nhớ của API.
    public const int ConnectionBufferSize = 100;

    private readonly ConcurrentDictionary<Guid, CustomerEventConnection> _connections = new();

    public int ConnectionCount => _connections.Count;

    public CustomerEventConnection Register(string conversationId)
    {
        // Mỗi tab có kết nối riêng. Hai tab của cùng hội thoại cùng nhận sự kiện của hội thoại đó.
        var connection = new CustomerEventConnection(Guid.NewGuid(), conversationId, this);
        _connections[connection.Id] = connection;
        return connection;
    }

    public void Broadcast(string conversationId, SupportEvent supportEvent)
    {
        // Dựng dòng một lần rồi chia cho mọi kết nối của hội thoại. Bản sao không cần thiết vì dòng chỉ đọc.
        var item = new CustomerStreamItem.Event(supportEvent.Name, supportEvent.Data);
        foreach (var connection in _connections.Values)
        {
            // Chỉ kết nối của đúng hội thoại nhận sự kiện. Kênh của khách khác không được lộ dữ liệu.
            if (connection.ConversationId != conversationId)
            {
                continue;
            }

            connection.Send(item);
        }
    }

    // Chỉ `CustomerEventConnection` gọi hàm này khi đóng, để hub không giữ kết nối đã đóng.
    internal void Remove(Guid connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }
}
