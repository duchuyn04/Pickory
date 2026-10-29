using NpgsqlTypes;

namespace Pickory.Modules.SupportDesk.Entities;

// Tiến độ của một yêu cầu gọi lại. `unreachable` nghĩa là nhân viên gọi mà không liên lạc được.
[PgName("CallbackRequestStatus")]
internal enum CallbackRequestStatus
{
    [PgName("pending")]
    Pending,

    [PgName("done")]
    Done,

    [PgName("unreachable")]
    Unreachable,
}
