using NpgsqlTypes;

namespace Pickory.Modules.Catalog.Entities;

// Phân loại lý do biến động tồn kho để phục vụ đối soát đơn hàng.
[PgName("InventoryMovementType")]
public enum InventoryMovementType
{
    [PgName("manual")]
    Manual,

    [PgName("order_reserve")]
    OrderReserve,

    [PgName("order_release")]
    OrderRelease,

    [PgName("return")]
    Return,
}
