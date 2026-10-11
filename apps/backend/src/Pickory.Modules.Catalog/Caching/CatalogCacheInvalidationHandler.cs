using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog.Caching;

// Xóa cache công khai khi sản phẩm hoặc tồn kho đổi. Sự kiện chỉ được phát sau khi transaction commit.
// Mọi đường ghi tồn kho (quản trị, giữ hàng, trả hàng) đều phát `StockChangedEvent`, nên module khác đi qua cùng điểm này.
public sealed class CatalogCacheInvalidationHandler(CatalogResponseCache cache)
    : IDomainEventHandler<ProductChangedEvent>, IDomainEventHandler<StockChangedEvent>
{
    public Task HandleAsync(ProductChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // Tạo, sửa, đổi trạng thái hoặc xóa sản phẩm (kèm SKU và ảnh) đều đổi danh sách công khai.
        cache.Invalidate();
        return Task.CompletedTask;
    }

    public Task HandleAsync(StockChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // Tồn kho đổi `inStock` và `minPrice` của sản phẩm trong danh sách.
        cache.Invalidate();
        return Task.CompletedTask;
    }
}
