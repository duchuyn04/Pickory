using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Data.Common;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Events;
using Pickory.Modules.Catalog.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog.Stock;

// Giữ hàng và trả hàng cho module `commerce`. Mọi thay đổi tồn kho đi qua `StockLedger`.
public sealed class StockService(CatalogDbContext database, IEventDispatcher dispatcher, TimeProvider timeProvider) : IStockService
{
    // Vùng khóa riêng cho khóa theo `orderId`. Tách khỏi `ProductWriteLockKey` để hai loại khóa không đụng nhau.
    private const int OrderLockNamespace = 8101;
    private const string ReservedBalancesSql = """
        SELECT "skuId", CAST(SUM(delta) AS integer)
        FROM inventory_movements
        WHERE "orderId" = @orderId AND type IN ('order_reserve', 'order_release')
        GROUP BY "skuId" HAVING SUM(delta) <> 0
        """;

    public async Task<ReserveResult> ReserveAsync(
        string orderId, IReadOnlyList<StockItem> items, StockCallerTransaction? caller, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        // Gộp mục trùng SKU trước khi mở transaction, để lỗi đầu vào không giữ khóa hay kết nối.
        var merged = StockRequests.MergeItems(items);
        if (merged.Count == 0)
        {
            return ReserveResult.Success;
        }

        return await RunAsync(caller, async (transaction, events) =>
        {
            await LockOrderAsync(transaction, orderId, cancellationToken);
            // Số dư khác không nghĩa là đơn đang giữ hàng, nên retry không được trừ kho thêm.
            if (await HasReservedAsync(transaction, orderId, cancellationToken))
            {
                return ReserveResult.Success;
            }

            var movements = merged.Select(item => new StockMovementInput(
                item.SkuId, -item.Quantity, InventoryMovementType.OrderReserve, orderId, null, $"Giữ hàng cho đơn {orderId}")).ToList();
            var result = await StockLedger.ApplyAsync(transaction, movements, Now(), cancellationToken);
            if (!result.Applied)
            {
                // Ledger chưa ghi gì khi thiếu hàng, nên bên gọi commit hay rollback đều không mất hàng.
                return ReserveResult.Short(result.Shortages);
            }

            EmitStockEvents(events, result);
            return ReserveResult.Success;
        }, cancellationToken);
    }

    public async Task ReleaseAsync(string orderId, StockCallerTransaction? caller, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        await RunAsync(caller, async (transaction, events) =>
        {
            await LockOrderAsync(transaction, orderId, cancellationToken);
            // Số dư gộp cả giữ và trả cho phép hoàn đúng hàng của chu kỳ hiện tại.
            var reserved = await ReadReservedAsync(transaction, orderId, cancellationToken);
            if (reserved.Count == 0)
            {
                return true;
            }

            var movements = reserved.Select(item => new StockMovementInput(
                item.SkuId, -item.Delta, InventoryMovementType.OrderRelease, orderId, null, $"Trả hàng đã giữ của đơn {orderId}")).ToList();
            var result = await StockLedger.ApplyAsync(transaction, movements, Now(), cancellationToken);
            if (!result.Applied)
            {
                throw new InvalidOperationException($"Không trả được hàng đã giữ của đơn {orderId}.");
            }

            EmitStockEvents(events, result);
            return true;
        }, cancellationToken);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static void EmitStockEvents(DomainEventCollector events, StockLedgerResult result)
    {
        // Mỗi SKU đổi tồn kho phát đúng một sự kiện. Collector chỉ phát sau khi bên sở hữu transaction commit.
        foreach (var change in result.Changes)
        {
            events.Emit(new StockChangedEvent(
                change.ProductId, change.SkuId, change.Stock, change.PreviousStock,
                change.ProductInStock, change.ProductMinPrice));
        }
    }

    // Chạy thao tác trong transaction của bên gọi, hoặc tự mở transaction khi bên gọi không truyền.
    private async Task<T> RunAsync<T>(
        StockCallerTransaction? caller, Func<DbTransaction, DomainEventCollector, Task<T>> action, CancellationToken cancellationToken)
    {
        if (caller is not null)
        {
            return await action(caller.Transaction, caller.Events);
        }

        return await CatalogTransaction.RunAsync(database, dispatcher,
            events => action(database.Database.CurrentTransaction!.GetDbTransaction(), events), cancellationToken);
    }

    // Khóa `pg_advisory_xact_lock` theo `orderId` chặn thao tác lặp vì DB không còn unique theo movement.
    private static async Task LockOrderAsync(DbTransaction transaction, string orderId, CancellationToken cancellationToken)
    {
        await using var command = Command(transaction, "SELECT pg_advisory_xact_lock(@namespace, hashtext(@orderId))");
        command.Parameters.AddWithValue("namespace", OrderLockNamespace);
        command.Parameters.AddWithValue("orderId", orderId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasReservedAsync(
        DbTransaction transaction, string orderId, CancellationToken cancellationToken)
    {
        // Chỉ kiểm tồn tại để retry không cấp phát danh sách số dư của mọi SKU.
        await using var command = Command(transaction, "SELECT EXISTS (" + ReservedBalancesSql + ")");
        command.Parameters.AddWithValue("orderId", orderId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<List<StockDelta>> ReadReservedAsync(
        DbTransaction transaction, string orderId, CancellationToken cancellationToken)
    {
        // Gộp mọi chu kỳ để số dư phản ánh hàng đang giữ, không phải tổng hàng từng giữ.
        await using var command = Command(transaction, ReservedBalancesSql);
        command.Parameters.AddWithValue("orderId", orderId);
        var reserved = new List<StockDelta>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            reserved.Add(new StockDelta(reader.GetString(0), reader.GetInt32(1)));
        }

        return reserved;
    }

    private static NpgsqlCommand Command(DbTransaction transaction, string sql)
    {
        var connection = (NpgsqlConnection)(transaction.Connection
            ?? throw new InvalidOperationException("Transaction không còn kết nối tới DB."));
        return new NpgsqlCommand(sql, connection, (NpgsqlTransaction)transaction);
    }
}
