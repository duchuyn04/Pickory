namespace Pickory.Modules.SupportDesk.Sessions;

// Dữ liệu phản hồi khi kết thúc phiên hỗ trợ CSKH thành công (200 OK).
internal sealed record SupportSessionCloseResponse(
    string SessionId,
    DateTime ClosedAt,
    string ClosedBy);
