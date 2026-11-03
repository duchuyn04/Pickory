using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Contracts.Events;

// Phát sau khi trạng thái một phiên đã commit. Kênh SSE của khách dùng sự kiện này.
// Khách chỉ nhận mã phiên, không nhận nhân viên giữ phiên hay ghi chú nội bộ.
public sealed record SupportSessionStatusChangedEvent(
    string SessionId,
    string ConversationId,
    SupportSessionStatus Status) : IDomainEvent;

// Trạng thái phiên mà khách cần biết. Mỗi trạng thái tương ứng một tên sự kiện SSE.
public enum SupportSessionStatus
{
    // Khách vừa chờ nhân viên, phiên chưa có người nhận.
    Waiting,

    // Một nhân viên đã nhận phiên.
    Assigned,

    // Phiên đã kết thúc. Khách chấm điểm sau tin hệ thống mời chấm điểm.
    Closed,
}
