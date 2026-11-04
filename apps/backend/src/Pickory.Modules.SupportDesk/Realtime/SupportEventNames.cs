namespace Pickory.Modules.SupportDesk.Realtime;

// Tên sự kiện SSE gửi cho nhân viên. Client lọc sự kiện theo đúng các tên này, nên task sau thêm tên mới vào đây.
internal static class SupportEventNames
{
    public const string PresenceChanged = "presence.changed";
    public const string Heartbeat = "heartbeat";

    // Khách vừa mở phiên chờ. Client đọc lại tab Đang chờ rồi hiện thông báo kèm âm báo.
    public const string SessionWaiting = "session.waiting";

    // Phiên chờ quá lâu mà chưa ai nhận. Client đọc lại tab Đang chờ rồi hiện thông báo nhắc lại.
    public const string SessionReminded = "session.reminded";
}
