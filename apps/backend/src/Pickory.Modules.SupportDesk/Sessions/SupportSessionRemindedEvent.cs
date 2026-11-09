using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Sessions;

// Phát sau khi module đã ghi xong tin nhắc lại cho một phiên chờ.
// Sự kiện nội bộ của module, vì chỉ kênh SSE của nhân viên dùng và module khác không cần biết.
internal sealed record SupportSessionRemindedEvent(string SessionId) : IDomainEvent;
