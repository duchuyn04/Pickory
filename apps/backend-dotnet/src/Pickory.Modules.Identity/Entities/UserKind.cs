using NpgsqlTypes;

namespace Pickory.Modules.Identity.Entities;

// Phân loại tài khoản trong hệ thống để áp dụng chính sách quyền tương ứng.
[PgName("UserKind")]
public enum UserKind
{
    [PgName("customer")]
    Customer,

    [PgName("staff")]
    Staff,
}
