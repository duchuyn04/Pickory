using Pickory.Modules.SupportDesk.Callbacks;
using Pickory.Modules.SupportDesk.Contracts.Events;
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
        IDomainEventHandler<CallbackRequestCreatedEvent>
{
    public async Task HandleAsync(
        SupportSessionStatusChangedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.Status is SupportSessionStatus.Closed)
        {
            // Tab Đang chờ của mọi nhân viên có thể đang hiện phiên này. Nhân viên tạm vắng cũng nhận để gỡ phiên khi quay lại.
            eventHub.Broadcast(
                new SupportEvent(SupportEventNames.SessionClosed, new SessionEventData(@event.SessionId)));
            return;
        }

        // Chỉ trạng thái chờ mới báo cho nhân viên sẵn sàng. Nhận phiên thuộc SD-06 và SD-08.
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

    private async Task BroadcastToAvailableStaffAsync(
        string eventName, string sessionId, CancellationToken cancellationToken)
    {
        // Đọc danh sách sẵn sàng lúc phát. Nhân viên có thể vừa đổi trạng thái sau khi phiên được mở.
        var availableStaffIds = await staffPresence.ListAvailableStaffIdsAsync(cancellationToken);
        // Dữ liệu chỉ có mã phiên. Client nhận sự kiện rồi đọc lại danh sách hàng chờ.
        eventHub.BroadcastToStaff(availableStaffIds, new SupportEvent(eventName, new SessionEventData(sessionId)));
    }
}
