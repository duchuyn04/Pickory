using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;

namespace Pickory.Modules.Commerce.Orders;

// Tạo dòng `order_status_history`. Mọi thao tác của khách và nhân viên ghi lịch sử cùng một cách.
internal static class OrderHistoryEntries
{
    // Thêm dòng vào cả đơn đang theo dõi và DbSet, để response dựng từ đơn thấy ngay dòng mới.
    public static void Add(
        CommerceDbContext database,
        Order order,
        OrderStatus fromStatus,
        OrderStatus toStatus,
        string? actorId,
        string reason,
        DateTime createdAt)
    {
        var history = new OrderStatusHistory
        {
            Id = Guid.NewGuid().ToString(),
            OrderId = order.Id,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ActorId = actorId,
            Reason = reason,
            CreatedAt = createdAt,
        };
        order.StatusHistory.Add(history);
        database.OrderStatusHistory.Add(history);
    }

    // Cắt về microsecond để thời điểm trong phản hồi khớp chính xác với giá trị đã lưu.
    public static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);
}
