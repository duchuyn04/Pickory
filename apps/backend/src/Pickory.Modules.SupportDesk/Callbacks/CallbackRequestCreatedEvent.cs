using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Phát sau khi dòng yêu cầu gọi lại đã commit. Sự kiện nội bộ của module, vì chỉ kênh sự kiện của nhân viên dùng.
internal sealed record CallbackRequestCreatedEvent(string CallbackRequestId) : IDomainEvent;
