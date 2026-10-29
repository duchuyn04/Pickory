using NpgsqlTypes;

namespace Pickory.Modules.SupportDesk.Entities;

// Bên gửi một tin trong hội thoại: khách, nhân viên hoặc hệ thống.
[PgName("SupportMessageRole")]
internal enum SupportMessageRole
{
    [PgName("customer")]
    Customer,

    [PgName("staff")]
    Staff,

    [PgName("system")]
    System,
}
