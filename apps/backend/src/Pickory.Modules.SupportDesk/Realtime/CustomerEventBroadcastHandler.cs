using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Realtime;

// Chuyển sự kiện miền đã commit tới kết nối SSE của đúng hội thoại.
internal sealed class CustomerEventBroadcastHandler(CustomerEventHub eventHub)
    : IDomainEventHandler<SupportMessageCreatedEvent>, IDomainEventHandler<SupportSessionStatusChangedEvent>
{
    public Task HandleAsync(SupportMessageCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Tin của khách không phát ngược qua kênh khách. Khách đã nhận tin trong response gửi tin và lịch sử.
        if (@event.Role is not (SupportMessageCreatedEvent.StaffRole or SupportMessageCreatedEvent.SystemRole))
        {
            return Task.CompletedTask;
        }

        // Dữ liệu công khai của tin. Không gửi `staffId` hay ghi chú nội bộ của phiên.
        eventHub.Broadcast(@event.ConversationId, new SupportEvent(
            CustomerEventNames.MessageCreated,
            new CustomerMessageCreatedEventData(
                @event.MessageId, @event.Role, @event.Body, @event.Action, @event.CreatedAt)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SupportSessionStatusChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // Dữ liệu công khai của phiên chỉ có mã phiên. Trạng thái nằm trong tên sự kiện.
        eventHub.Broadcast(@event.ConversationId, new SupportEvent(
            ReadEventName(@event.Status),
            new CustomerSessionEventData(@event.SessionId)));
        return Task.CompletedTask;
    }

    // Mỗi trạng thái có một tên sự kiện riêng. Client nghe ba tên này để đổi trạng thái khung chat.
    private static string ReadEventName(SupportSessionStatus status) => status switch
    {
        SupportSessionStatus.Waiting => CustomerEventNames.SessionWaiting,
        SupportSessionStatus.Assigned => CustomerEventNames.SessionAssigned,
        SupportSessionStatus.Closed => CustomerEventNames.SessionClosed,
        // Enum đóng nên trạng thái lạ là lỗi lập trình. Bỏ qua sự kiện sẽ làm khách thấy sai trạng thái phiên.
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Trạng thái phiên không có sự kiện SSE."),
    };
}
