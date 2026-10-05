using NpgsqlTypes;

namespace Pickory.Modules.Catalog.Persistence;

// Giữ nhãn PostgreSQL để trạng thái sản phẩm không bị lưu thành số.
[PgName("ProductStatus")]
public enum ProductStatus
{
    [PgName("draft")]
    Draft,

    [PgName("active")]
    Active,

    [PgName("hidden")]
    Hidden,

    [PgName("discontinued")]
    Discontinued,
}

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
