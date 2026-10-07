using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Events;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Catalog.Stock;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Catalog.Features.Inventory;

public sealed class InventoryHandler(
    CatalogDbContext database,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher,
    TimeProvider timeProvider)
{
    public async Task<AdjustStockResponse> AdjustStockAsync(
        Actor actor, string skuId, AdjustStockRequest request, CancellationToken cancellationToken)
    {
        // Lý do lưu bản đã cắt khoảng trắng, khớp với độ dài mà validator đã kiểm.
        var reason = request.Reason!.Trim();
        // Ledger, audit và sự kiện cùng một transaction. Lỗi ở bước nào cũng hoàn nguyên cả ba.
        return await CatalogTransaction.RunAsync(database, dispatcher, async events =>
        {
            var transaction = database.Database.CurrentTransaction!.GetDbTransaction();
            var movement = new StockMovementInput(skuId, request.Delta, request.MovementType, null, actor.UserId, reason);
            var result = await StockLedger.ApplyAsync(transaction, [movement], timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            if (result.MissingSkuIds.Count > 0)
            {
                throw new AppException("SKU_NOT_FOUND", 404, "Không tìm thấy biến thể.");
            }

            // Điều chỉnh làm tồn kho âm thì ledger chưa ghi gì. API báo 409 để admin sửa số lượng.
            if (result.Shortages.Count > 0)
            {
                var shortage = result.Shortages[0];
                throw new AppException("INSUFFICIENT_STOCK", 409,
                    $"Tồn kho hiện có {shortage.Available}, không thể giảm {shortage.Requested}.");
            }

            var change = result.Changes[0];
            var typeName = MovementTypeNames.ToName(request.MovementType);
            await auditLogWriter.WriteAsync(
                new AuditLogEntry(actor.UserId!, "catalog.sku.stock_adjust", "sku", skuId,
                    new { stock = change.PreviousStock },
                    new { stock = change.Stock, delta = request.Delta, type = typeName, reason }),
                transaction, cancellationToken);
            events.Emit(new StockChangedEvent(skuId, change.Stock, change.PreviousStock));
            return new AdjustStockResponse(skuId, change.Stock, new InventoryMovementResponse(
                change.MovementId, request.Delta, typeName, null, actor.UserId, reason, change.CreatedAt));
        }, cancellationToken);
    }

    public async Task<PagedResponse<InventoryMovementResponse>> GetMovementsAsync(
        string skuId, PageQuery page, CancellationToken cancellationToken)
    {
        // SKU thiếu nhận 404 thay vì danh sách rỗng, để giao diện phân biệt SKU sai với SKU chưa có lịch sử.
        if (!await database.Skus.AnyAsync(sku => sku.Id == skuId, cancellationToken))
        {
            throw new AppException("SKU_NOT_FOUND", 404, "Không tìm thấy biến thể.");
        }

        var movements = database.InventoryMovements.AsNoTracking().Where(movement => movement.SkuId == skuId);
        var total = await movements.CountAsync(cancellationToken);
        // Thứ tự phụ theo định danh giữ phân trang ổn định khi hai movement cùng thời điểm.
        var items = await movements.OrderByDescending(movement => movement.CreatedAt).ThenByDescending(movement => movement.Id)
            .Skip(page.Skip).Take(page.Limit).ToListAsync(cancellationToken);
        return new PagedResponse<InventoryMovementResponse>(
            items.Select(movement => new InventoryMovementResponse(movement.Id, movement.Delta,
                MovementTypeNames.ToName(movement.Type), movement.OrderId, movement.ActorId, movement.Reason, movement.CreatedAt)).ToList(),
            total, page.Page, page.Limit);
    }

    public async Task<PagedResponse<LowStockSkuResponse>> GetLowStockAsync(PageQuery page, CancellationToken cancellationToken)
    {
        // Cùng điều kiện với bộ lọc `lowStock` của danh sách sản phẩm. SKU tắt không bán nên không cần nhập thêm.
        var lowStock = database.Skus.AsNoTracking()
            .Where(sku => sku.Enabled && sku.Stock <= CatalogInventoryDefaults.LowStockThreshold);
        var total = await lowStock.CountAsync(cancellationToken);
        // Thứ tự phụ theo mã SKU giữ phân trang ổn định khi nhiều SKU cùng số tồn.
        var rows = await lowStock.OrderBy(sku => sku.Stock).ThenBy(sku => sku.Code)
            .Skip(page.Skip).Take(page.Limit)
            .Select(sku => new { sku.Id, sku.Code, sku.ProductId, ProductName = sku.Product!.Name, sku.OptionValues, sku.Stock })
            .ToListAsync(cancellationToken);
        return new PagedResponse<LowStockSkuResponse>(
            rows.Select(row => new LowStockSkuResponse(row.Id, row.Code, row.ProductId, row.ProductName,
                row.OptionValues.Deserialize<Dictionary<string, string>>()!, row.Stock)).ToList(),
            total, page.Page, page.Limit);
    }
}
