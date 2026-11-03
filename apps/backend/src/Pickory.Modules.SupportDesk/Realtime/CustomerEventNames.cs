namespace Pickory.Modules.SupportDesk.Realtime;

// Tên sự kiện SSE gửi cho khách. Client lọc sự kiện theo đúng các tên này, nên task sau thêm tên mới vào đây.
internal static class CustomerEventNames
{
    // Tin của nhân viên và tin hệ thống dùng chung một tên. Client đọc `role` để chọn kiểu hiển thị.
    public const string MessageCreated = "message.created";

    public const string SessionWaiting = "session.waiting";
    public const string SessionAssigned = "session.assigned";
    public const string SessionClosed = "session.closed";
}
