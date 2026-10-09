using NpgsqlTypes;

namespace Pickory.Modules.Catalog.Entities;

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
