using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;

namespace Pickory.Modules.Catalog.Stock;

// Một thay đổi tồn kho cần ghi. `Delta` âm là lấy hàng ra.
internal sealed record StockMovementInput(
    string SkuId, int Delta, InventoryMovementType Type, string? OrderId, string? ActorId, string Reason);

// Kết quả ghi của một SKU: tồn kho sau và trước, kèm movement vừa tạo và giá trị tính sẵn của sản phẩm.
internal sealed record StockChange(
    string ProductId,
    string SkuId,
    int Stock,
    int PreviousStock,
    string MovementId,
    DateTime CreatedAt,
    bool ProductInStock = false,
    int? ProductMinPrice = null);

// Kết quả của `StockLedger.ApplyAsync`. Khi `Applied` là false, ledger chưa ghi gì.
internal sealed record StockLedgerResult(
    IReadOnlyList<StockShortage> Shortages, IReadOnlyList<string> MissingSkuIds, IReadOnlyList<StockChange> Changes)
{
    public bool Applied => Shortages.Count == 0 && MissingSkuIds.Count == 0;
}

// Nơi duy nhất đổi cột `stock` của SKU (Decision 5). Mỗi lần đổi ghi movement và tính lại `minPrice`, `inStock` của sản phẩm.
internal static class StockLedger
{
    public static async Task<StockLedgerResult> ApplyAsync(
        DbTransaction transaction, IReadOnlyList<StockMovementInput> movements, DateTime now, CancellationToken cancellationToken)
    {
        // Ledger ghi bằng SQL trên chính kết nối của transaction. Nhờ vậy ghi cùng transaction với bên gọi.
        var connection = (NpgsqlConnection)(transaction.Connection
            ?? throw new InvalidOperationException("Transaction không còn kết nối tới DB."));
        var npgsqlTransaction = (NpgsqlTransaction)transaction;

        var ordered = movements.OrderBy(movement => movement.SkuId, StringComparer.Ordinal).ToList();
        if (ordered.Select(movement => movement.SkuId).Distinct(StringComparer.Ordinal).Count() != ordered.Count)
        {
            throw new ArgumentException("Mỗi SKU chỉ được có một movement trong một lần ghi.", nameof(movements));
        }

        // Khóa dòng sản phẩm trước, theo thứ tự `id`. Mọi đường ghi dùng cùng thứ tự `products` rồi `skus`, nên không khóa chéo nhau.
        // Đọc `deletedAt` trong cùng câu lệnh khóa để trạng thái xóa không đổi tới hết transaction.
        var productIdsBySku = await ReadProductIdsAsync(connection, npgsqlTransaction, ordered.Select(m => m.SkuId).ToArray(), cancellationToken);
        var deletedProductIds = await LockProductsAsync(connection, npgsqlTransaction, productIdsBySku.Values.Distinct(StringComparer.Ordinal).ToArray(), cancellationToken);

        // Khóa dòng SKU sau khi đã giữ khóa sản phẩm. Tồn kho đọc ở đây là bản sau khi transaction khác đã commit.
        var current = await LockSkusAsync(connection, npgsqlTransaction, ordered.Select(m => m.SkuId).ToArray(), cancellationToken);
        var available = current.ToDictionary(pair => pair.Key, pair => pair.Value.Stock, StringComparer.Ordinal);
        // Sản phẩm đã xóa mềm không còn hàng bán được. Đặt tồn khả dụng về 0 để bước kiểm thiếu hàng chặn giữ hàng.
        foreach (var movement in ordered)
        {
            if (movement.Type == InventoryMovementType.OrderReserve
                && productIdsBySku.TryGetValue(movement.SkuId, out var productId)
                && deletedProductIds.Contains(productId))
            {
                available[movement.SkuId] = 0;
            }
        }

        var shortages = StockShortages.Find(ordered.Select(m => new StockDelta(m.SkuId, m.Delta)).ToList(), available);
        var missing = ordered.Select(m => m.SkuId).Where(skuId => !current.ContainsKey(skuId)).ToList();
        // Kiểm đủ hàng trước khi ghi. Thiếu hàng thì không để lại thay đổi nào trong transaction của bên gọi.
        if (shortages.Count > 0 || missing.Count > 0)
        {
            return new StockLedgerResult(shortages, missing, []);
        }

        var changes = new List<StockChange>();
        foreach (var movement in ordered)
        {
            // UPDATE có điều kiện là lớp chặn tồn kho âm thứ hai, sau bước kiểm ở trên và `CHECK` của DB.
            var stockAfter = await UpdateStockAsync(connection, npgsqlTransaction, movement, now, cancellationToken);
            var movementId = Guid.NewGuid().ToString("N");
            await InsertMovementAsync(connection, npgsqlTransaction, movementId, movement, now, cancellationToken);
            changes.Add(new StockChange(
                productIdsBySku[movement.SkuId], movement.SkuId, stockAfter, stockAfter - movement.Delta, movementId, now));
        }

        // Tính lại giá trị tính sẵn trong cùng transaction. Khóa sản phẩm đã giữ từ đầu nên không ghi đè bằng dữ liệu cũ.
        var pricingByProduct = new Dictionary<string, DisplayPrice>(StringComparer.Ordinal);
        foreach (var productId in productIdsBySku.Values.Distinct(StringComparer.Ordinal))
        {
            pricingByProduct[productId] = await RecomputeProductAsync(connection, npgsqlTransaction, productId, cancellationToken);
        }

        // Gắn kết quả tính sẵn vào từng lần đổi. Mọi SKU của cùng sản phẩm nhận cùng một cặp giá và trạng thái còn hàng.
        var pricedChanges = changes.Select(change => change with
        {
            ProductInStock = pricingByProduct[change.ProductId].InStock,
            ProductMinPrice = pricingByProduct[change.ProductId].MinPrice,
        }).ToList();
        return new StockLedgerResult([], [], pricedChanges);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        return new NpgsqlCommand(sql, connection, transaction);
    }

    // Cột thời gian của DB không có múi giờ. Npgsql từ chối giá trị mang kiểu UTC, nên đổi sang kiểu không xác định.
    private static NpgsqlParameter Timestamp(string name, DateTime value)
    {
        return new NpgsqlParameter(name, NpgsqlDbType.Timestamp) { Value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified) };
    }

    // `productId` của SKU không bao giờ đổi, nên đọc không khóa vẫn cho kết quả đúng.
    private static async Task<Dictionary<string, string>> ReadProductIdsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string[] skuIds, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, "SELECT id, \"productId\" FROM skus WHERE id = ANY(@ids)");
        command.Parameters.AddWithValue("ids", skuIds);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    private static async Task<HashSet<string>> LockProductsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string[] productIds, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction,
            "SELECT id, \"deletedAt\" FROM products WHERE id = ANY(@ids) ORDER BY id FOR UPDATE");
        command.Parameters.AddWithValue("ids", productIds);
        var deletedProductIds = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // Dòng đã xóa mềm vẫn phải khóa để DELETE và reserve không chạy chồng lên nhau.
            if (!reader.IsDBNull(1))
            {
                deletedProductIds.Add(reader.GetString(0));
            }
        }

        return deletedProductIds;
    }

    private static async Task<Dictionary<string, (int Stock, string ProductId)>> LockSkusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string[] skuIds, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction,
            "SELECT id, stock, \"productId\" FROM skus WHERE id = ANY(@ids) ORDER BY id FOR UPDATE");
        command.Parameters.AddWithValue("ids", skuIds);
        var result = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = (reader.GetInt32(1), reader.GetString(2));
        }

        return result;
    }

    private static async Task<int> UpdateStockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, StockMovementInput movement, DateTime now, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction,
            "UPDATE skus SET stock = stock + @delta, \"updatedAt\" = @now WHERE id = @id AND stock + @delta >= 0 RETURNING stock");
        command.Parameters.AddWithValue("delta", movement.Delta);
        command.Parameters.AddWithValue("id", movement.SkuId);
        command.Parameters.Add(Timestamp("now", now));
        var stockAfter = await command.ExecuteScalarAsync(cancellationToken);
        // Dòng SKU đã khóa và đã kiểm đủ hàng, nên không cập nhật được dòng nào nghĩa là bất biến bị phá.
        return stockAfter is int value
            ? value
            : throw new InvalidOperationException($"Không cập nhật được tồn kho của SKU {movement.SkuId}.");
    }

    private static async Task InsertMovementAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string movementId, StockMovementInput movement,
        DateTime now, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction,
            "INSERT INTO inventory_movements (id, \"skuId\", delta, type, \"orderId\", \"actorId\", reason, \"createdAt\") "
            + "VALUES (@id, @skuId, @delta, CAST(@type AS \"InventoryMovementType\"), @orderId, @actorId, @reason, @createdAt)");
        command.Parameters.AddWithValue("id", movementId);
        command.Parameters.AddWithValue("skuId", movement.SkuId);
        command.Parameters.AddWithValue("delta", movement.Delta);
        command.Parameters.AddWithValue("type", MovementTypeNames.ToName(movement.Type));
        command.Parameters.AddWithValue("orderId", (object?)movement.OrderId ?? DBNull.Value);
        command.Parameters.AddWithValue("actorId", (object?)movement.ActorId ?? DBNull.Value);
        command.Parameters.AddWithValue("reason", movement.Reason);
        command.Parameters.Add(Timestamp("createdAt", now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DisplayPrice> RecomputeProductAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string productId, CancellationToken cancellationToken)
    {
        // Hàm gọi đã khóa dòng sản phẩm. Đọc SKU ở đây thấy cả thay đổi chưa commit của chính transaction này.
        var skus = new List<SkuPricingInput>();
        await using (var readCommand = Command(connection, transaction,
            "SELECT price, stock, enabled FROM skus WHERE \"productId\" = @id"))
        {
            readCommand.Parameters.AddWithValue("id", productId);
            await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                skus.Add(new SkuPricingInput(reader.GetInt32(0), reader.GetInt32(1), reader.GetBoolean(2)));
            }
        }

        // Dùng chung hàm tính giá với lúc lưu sản phẩm, để hai nơi không lệch nhau.
        var display = ProductPricing.ComputeDisplayPrice(skus);
        await using var updateCommand = Command(connection, transaction,
            "UPDATE products SET \"minPrice\" = @minPrice, \"inStock\" = @inStock WHERE id = @id");
        updateCommand.Parameters.AddWithValue("minPrice", (object?)display.MinPrice ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("inStock", display.InStock);
        updateCommand.Parameters.AddWithValue("id", productId);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        return display;
    }
}
