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

    // Chỉ nhận đúng bốn tên của `ToName`. Dùng `switch` thay cho `Enum.TryParse`, vì `Enum.TryParse` nhận cả chuỗi số và chữ hoa.
    public static bool TryParse(string name, out InventoryMovementType type)
    {
        switch (name)
        {
            case "manual":
                type = InventoryMovementType.Manual;
                return true;
            case "order_reserve":
                type = InventoryMovementType.OrderReserve;
                return true;
            case "order_release":
                type = InventoryMovementType.OrderRelease;
                return true;
            case "return":
                type = InventoryMovementType.Return;
                return true;
            default:
                type = default;
                return false;
        }
    }
}
