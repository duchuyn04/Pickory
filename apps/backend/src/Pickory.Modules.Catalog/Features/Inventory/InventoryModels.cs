namespace Pickory.Modules.Catalog.Features.Inventory;

// Một dòng lịch sử tồn kho. `Type` dùng nhãn của DB, ví dụ `order_reserve`.
public sealed record InventoryMovementResponse(
    string Id, int Delta, string Type, string? OrderId, string? ActorId, string Reason, DateTime CreatedAt);

// Kết quả điều chỉnh: tồn kho mới và movement vừa ghi.
public sealed record AdjustStockResponse(string SkuId, int Stock, InventoryMovementResponse Movement);

// SKU đang bật có tồn kho dưới hoặc bằng ngưỡng sắp hết hàng.
public sealed record LowStockSkuResponse(
    string SkuId, string Code, string ProductId, string ProductName, IReadOnlyDictionary<string, string> OptionValues, int Stock);
