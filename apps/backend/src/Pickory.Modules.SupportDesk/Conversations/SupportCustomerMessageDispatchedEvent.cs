using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Conversations;

// Sự kiện nội bộ khi tin của khách được gửi trong phiên có nhân viên.
internal sealed record SupportCustomerMessageDispatchedEvent(
    string SessionId,
    string ConversationId,
    string StaffId) : IDomainEvent;
