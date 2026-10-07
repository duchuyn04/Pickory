using System.Text.Json.Serialization;
using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Features.Inventory;

// Body điều chỉnh tồn kho bằng tay. `Type` bỏ trống nghĩa là `manual`.
public sealed record AdjustStockRequest(int Delta, string? Reason, string? Type = null)
{
    // Loại movement đã chuẩn hóa. Validator đảm bảo `Type` chỉ có thể là `manual`, `return` hoặc bỏ trống.
    [JsonIgnore]
    public InventoryMovementType MovementType => Type == AdjustStockTypes.Return
        ? InventoryMovementType.Return
        : InventoryMovementType.Manual;
}

// Tên loại movement mà điều chỉnh tay được dùng. Loại của đơn hàng chỉ do `IStockService` ghi.
public static class AdjustStockTypes
{
    public const string Manual = "manual";
    public const string Return = "return";
}
