using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Stock;

// Tên loại movement trong DB và API. Khớp nhãn của enum PostgreSQL `InventoryMovementType`.
public static class MovementTypeNames
{
    public static string ToName(InventoryMovementType type) => type switch
    {
        InventoryMovementType.Manual => "manual",
        InventoryMovementType.OrderReserve => "order_reserve",
        InventoryMovementType.OrderRelease => "order_release",
        InventoryMovementType.Return => "return",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Loại movement không hợp lệ."),
    };
}
