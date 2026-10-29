namespace Pickory.Modules.SupportDesk.Realtime;

// Tên sự kiện SSE gửi cho nhân viên. Client lọc sự kiện theo đúng các tên này, nên task sau thêm tên mới vào đây.
internal static class SupportEventNames
{
    public const string PresenceChanged = "presence.changed";
    public const string Heartbeat = "heartbeat";
}
