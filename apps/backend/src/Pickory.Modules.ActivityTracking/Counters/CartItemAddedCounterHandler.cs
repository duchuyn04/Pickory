using Pickory.Modules.ActivityTracking.Metrics;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.Modules.ActivityTracking.Counters;

// Đếm một lượt thêm giỏ cho mỗi `CartItemAddedEvent`. Số lượng của dòng không ảnh hưởng chỉ số.
internal sealed class CartItemAddedCounterHandler(DailyCounterWriter counterWriter) : IDomainEventHandler<CartItemAddedEvent>
{
    public async Task HandleAsync(CartItemAddedEvent @event, CancellationToken cancellationToken = default)
    {
        // Handler ghi bộ đếm không theo token của bộ phát, vì giỏ đã commit nên lượt thêm vẫn hợp lệ khi client ngắt kết nối. Handler không bắt exception, vì `InProcessEventDispatcher` đã ghi log và giữ luồng giỏ thành công.
        await counterWriter.IncrementAsync(ActivityMetricNames.AddToCart, CancellationToken.None);
    }
}
