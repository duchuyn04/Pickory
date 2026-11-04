using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Modules.SupportDesk.Sessions;
using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Realtime;

// Chuyển sự kiện miền đã commit tới kết nối của nhân viên đang sẵn sàng.
// Nhân viên tạm vắng không nhận thông báo phiên chờ, vì họ không nhận được phiên nào lúc này.
internal sealed class StaffSessionEventBroadcastHandler(
    StaffPresenceService staffPresence,
    SupportEventHub eventHub)
    : IDomainEventHandler<SupportSessionStatusChangedEvent>, IDomainEventHandler<SupportSessionRemindedEvent>
{
    public async Task HandleAsync(
        SupportSessionStatusChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // Chỉ trạng thái chờ mới báo cho nhân viên. Nhận phiên và kết thúc phiên thuộc SD-06 và SD-08.
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

    private async Task BroadcastToAvailableStaffAsync(
        string eventName, string sessionId, CancellationToken cancellationToken)
    {
        // Đọc danh sách sẵn sàng lúc phát. Nhân viên có thể vừa đổi trạng thái sau khi phiên được mở.
        var availableStaffIds = await staffPresence.ListAvailableStaffIdsAsync(cancellationToken);
        // Dữ liệu chỉ có mã phiên. Client nhận sự kiện rồi đọc lại danh sách hàng chờ.
        eventHub.BroadcastToStaff(availableStaffIds, new SupportEvent(eventName, new SessionEventData(sessionId)));
    }
}
