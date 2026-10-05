using NpgsqlTypes;

namespace Pickory.Modules.Identity.Entities;

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
