using System.Collections.Concurrent;

namespace Pickory.Modules.Catalog.Realtime;

// Giữ các kết nối SSE trong bộ nhớ tiến trình. MVP chạy một instance nên không cần bus giữa nhiều tiến trình.
internal sealed class StockEventHub
{
    // Giới hạn số sự kiện chờ của mỗi kết nối. Client chậm không làm đầy bộ nhớ của API.
    public const int ConnectionBufferSize = 100;

    private readonly ConcurrentDictionary<Guid, StockEventConnection> _connections = new();

    public StockEventConnection Register()
    {
        // Mỗi tab có kết nối riêng. Đóng tab này không ảnh hưởng tab khác.
        var connection = new StockEventConnection(Guid.NewGuid(), this);
        _connections[connection.Id] = connection;
        return connection;
    }

    public void Broadcast(StockEvent stockEvent)
    {
        // Duyệt bản chụp của từ điển. Kết nối đóng giữa lúc phát không làm hỏng vòng lặp.
        foreach (var connection in _connections.Values)
        {
            connection.Send(stockEvent);
        }
    }

    // Chỉ `StockEventConnection` gọi hàm này khi đóng, để hub không giữ kết nối đã đóng.
    internal void Remove(Guid connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }
}
