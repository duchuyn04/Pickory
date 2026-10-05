using NpgsqlTypes;

namespace Pickory.Modules.Identity.Persistence;

// Phân loại tài khoản trong hệ thống để áp dụng chính sách quyền tương ứng.
[PgName("UserKind")]
public enum UserKind
{
    [PgName("customer")]
    Customer,

    [PgName("staff")]
    Staff,
}

// Mục đích sử dụng của mã token gửi qua email để giới hạn phạm vi thao tác.
[PgName("EmailTokenPurpose")]
public enum EmailTokenPurpose
{
    [PgName("verify")]
    Verify,

    [PgName("reset")]
    Reset,

    [PgName("invite")]
    Invite,
}
