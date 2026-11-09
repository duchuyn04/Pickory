using Pickory.Modules.SupportDesk.Callbacks;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Modules.SupportDesk.Sessions;
using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Realtime;

// Chuyển sự kiện miền đã commit tới kết nối của nhân viên.
// Phiên chờ chỉ tới nhân viên sẵn sàng, còn phiên đóng và yêu cầu gọi lại tới mọi kết nối.
internal sealed class StaffSessionEventBroadcastHandler(
    StaffPresenceService staffPresence,
    SupportEventHub eventHub)
    : IDomainEventHandler<SupportSessionStatusChangedEvent>,
        IDomainEventHandler<SupportSessionRemindedEvent>,
        IDomainEventHandler<CallbackRequestCreatedEvent>,
        IDomainEventHandler<SupportCustomerMessageDispatchedEvent>
{
    public async Task HandleAsync(
        SupportSessionStatusChangedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.Status is SupportSessionStatus.Closed or SupportSessionStatus.Assigned)
        {
            // Tab Đang chờ của mọi nhân viên có thể đang hiện phiên này. Gửi tới mọi kết nối để gỡ khỏi tab Đang chờ.
            var eventName = @event.Status is SupportSessionStatus.Closed
                ? SupportEventNames.SessionClosed
                : SupportEventNames.SessionAssigned;
            eventHub.Broadcast(
                new SupportEvent(eventName, new SessionEventData(@event.SessionId)));
            return;
        }

        // Chỉ trạng thái chờ mới phát cho nhân viên sẵn sàng.
        if (@event.Status is not SupportSessionStatus.Waiting)
        {
            return;
        }

        await BroadcastToAvailableStaffAsync(
            SupportEventNames.SessionWaiting, @event.SessionId, cancellationToken);
    }

    public Task HandleAsync(SupportSessionRemindedEvent @event, CancellationToken cancellationToken = default)
    {
        // Nhắc lại cũng chỉ tới nhân viên sẵn sàng, vì người tạm vắng không vào hàng chờ để nhận phiên.
        return BroadcastToAvailableStaffAsync(
            SupportEventNames.SessionReminded, @event.SessionId, cancellationToken);
    }

    public Task HandleAsync(CallbackRequestCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Luồng sự kiện của nhân viên đã yêu cầu `conversations:read`, nên mọi kết nối đều có quyền xem tab Gọi lại.
        eventHub.Broadcast(new SupportEvent(
            SupportEventNames.CallbackCreated, new CallbackCreatedEventData(@event.CallbackRequestId)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SupportCustomerMessageDispatchedEvent @event, CancellationToken cancellationToken = default)
    {
        // Chỉ gửi sự kiện tin mới cho nhân viên đang giữ phiên.
        eventHub.BroadcastToStaff([@event.StaffId], new SupportEvent(
            SupportEventNames.MessageCreated,
            new StaffMessageCreatedEventData(@event.SessionId, @event.ConversationId)));
        return Task.CompletedTask;
    }
    private async Task BroadcastToAvailableStaffAsync(
        string eventName, string sessionId, CancellationToken cancellationToken)
    {
        // Đọc danh sách sẵn sàng lúc phát. Nhân viên có thể vừa đổi trạng thái sau khi phiên được mở.
        var availableStaffIds = await staffPresence.ListAvailableStaffIdsAsync(cancellationToken);
        // Dữ liệu chỉ có mã phiên. Client nhận sự kiện rồi đọc lại danh sách hàng chờ.
        eventHub.BroadcastToStaff(availableStaffIds, new SupportEvent(eventName, new SessionEventData(sessionId)));
    }
}
