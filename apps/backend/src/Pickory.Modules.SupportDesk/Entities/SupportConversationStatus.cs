using NpgsqlTypes;

namespace Pickory.Modules.SupportDesk.Entities;

// Trạng thái hội thoại của khách. Hội thoại im lặng 24 giờ thì chuyển sang `closed`.
[PgName("SupportConversationStatus")]
internal enum SupportConversationStatus
{
    [PgName("open")]
    Open,

    [PgName("closed")]
    Closed,
}
