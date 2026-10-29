using NpgsqlTypes;

namespace Pickory.Modules.SupportDesk.Entities;

// Trạng thái trực của nhân viên CSKH. Phiên chỉ tính là trong giờ khi có ít nhất một nhân viên `available`.
[PgName("StaffPresenceStatus")]
internal enum StaffPresenceStatus
{
    [PgName("available")]
    Available,

    [PgName("away")]
    Away,
}
