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

    // Phiên vừa kết thúc. Tab Đang chờ của mọi nhân viên có thể đang hiện phiên này, nên sự kiện gửi tới mọi kết nối.
    public const string SessionClosed = "session.closed";
    // Phiên vừa có nhân viên nhận hoặc chuyển. Gửi tới mọi kết nối để gỡ khỏi tab Đang chờ.
    public const string SessionAssigned = "session.assigned";

    // Khách vừa gửi yêu cầu gọi lại. Client đọc lại tab Gọi lại rồi hiện thông báo kèm âm báo.
    public const string CallbackCreated = "callback.created";

    // Khách vừa gửi tin mới trong phiên có nhân viên. Client đọc lại lịch sử.
    public const string MessageCreated = "message.created";
}
