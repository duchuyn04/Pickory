using Pickory.Modules.ActivityTracking.Metrics;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.Modules.ActivityTracking.Counters;

// Đếm một đơn cho mỗi `OrderPlacedEvent`, với mọi cách thanh toán. Tổng tiền và cách thanh toán không ảnh hưởng chỉ số.
internal sealed class OrderPlacedCounterHandler(DailyCounterWriter counterWriter) : IDomainEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken = default)
    {
        // Handler ghi bộ đếm không theo token của bộ phát, vì đơn đã commit nên lượt đặt vẫn hợp lệ khi client ngắt kết nối. Handler không bắt exception, vì `InProcessEventDispatcher` đã ghi log và giữ luồng đặt đơn thành công.
        await counterWriter.IncrementAsync(ActivityMetricNames.OrderPlaced, CancellationToken.None);
    }
}
