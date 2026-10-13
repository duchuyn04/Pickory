using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Shared.Events;

// Contracts cũng có kiểu `Shipment` cho bên gọi. Lớp này làm việc với entity của module.
using Shipment = Pickory.Modules.Shipping.Entities.Shipment;

namespace Pickory.Modules.Shipping.Shipments;

// Áp trạng thái GHN lên vận đơn. Webhook và job đồng bộ dùng chung lớp này để cùng một luật chống trùng và đổi `ref`.
internal sealed class ShipmentStatusUpdater(
    ShippingDbContext database,
    IEventDispatcher eventDispatcher,
    TimeProvider timeProvider,
    ILogger<ShipmentStatusUpdater> logger)
{
    // `shipment` phải là bản đọc không theo dõi (`AsNoTracking`). Hàm so `RawStatus` của bản này với DB lúc ghi để biết bên khác đã ghi trước chưa.
    public async Task<ShipmentStatusUpdateOutcome> ApplyAsync(
        Shipment shipment, string ghnRawStatus, ShipmentEventSource source)
    {
        // Trạng thái lạ không được ghi đè trạng thái cũ. Ghi log để người vận hành bổ sung bảng ánh xạ.
        var newStatus = ShipmentStatusMapper.Map(ghnRawStatus);
        if (newStatus is null)
        {
            logger.LogWarning(
                "GHN trả trạng thái chưa có trong bảng ánh xạ. Mã vận đơn GHN {TrackingCode}, trạng thái gốc {RawStatus}.",
                shipment.TrackingCode,
                ghnRawStatus);
            return ShipmentStatusUpdateOutcome.UnknownStatus;
        }

        // Cùng trạng thái gốc đã lưu nghĩa là webhook gửi trùng hoặc job tra lại. Không ghi gì và không phát sự kiện.
        if (string.Equals(shipment.RawStatus, ghnRawStatus, StringComparison.Ordinal))
        {
            return ShipmentStatusUpdateOutcome.NoChange;
        }

        // Vận đơn đã kết thúc giữ trạng thái cuối. Cập nhật muộn hoặc sai thứ tự không kéo vận đơn đã giao hay đã hủy quay lại.
        if (shipment.Status.IsTerminal())
        {
            logger.LogWarning(
                "Bỏ qua trạng thái GHN cho vận đơn đã kết thúc. Mã vận đơn GHN {TrackingCode}, trạng thái đã lưu {Status}, trạng thái gốc {RawStatus}.",
                shipment.TrackingCode,
                shipment.Status,
                ghnRawStatus);
            return ShipmentStatusUpdateOutcome.NoChange;
        }

        var now = timeProvider.GetUtcNow();
        var statusChanged = newStatus.Value != shipment.Status;
        var events = new DomainEventCollector(eventDispatcher);

        // GHN đã trả trạng thái, nên các bước lưu dùng `CancellationToken.None`. Request bị hủy giữa chừng không làm DB lệch GHN.
        await using (var transaction = await database.Database.BeginTransactionAsync(CancellationToken.None))
        {
            // Hủy thì đổi `ref` để mã gốc rảnh cho vận đơn mới, cùng cách với hủy bằng tay.
            var newRef = shipment.Ref;
            if (newStatus.Value == ShipmentStatus.Cancelled)
            {
                var cancelledCount = await CancelledShipmentRef.CountAsync(database, shipment.Ref, CancellationToken.None);
                newRef = CancelledShipmentRef.Build(shipment.Ref, cancelledCount);
            }

            // Chỉ ghi khi `RawStatus` trong DB vẫn là giá trị đã đọc. Bên ghi trước thắng, bên sau không ghi lịch sử và không phát sự kiện.
            var rowsUpdated = await database.Shipments
                .Where(row => row.Id == shipment.Id && row.RawStatus == shipment.RawStatus)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.Status, newStatus.Value)
                        .SetProperty(row => row.RawStatus, ghnRawStatus)
                        .SetProperty(row => row.Ref, newRef)
                        .SetProperty(row => row.UpdatedAt, now.UtcDateTime),
                    CancellationToken.None);
            if (rowsUpdated == 0)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return ShipmentStatusUpdateOutcome.LostRace;
            }

            // Dòng lịch sử cùng transaction với trạng thái mới. Mọi lần đổi trạng thái gốc đều có một dòng.
            database.ShipmentEvents.Add(new ShipmentEvent
            {
                ShipmentId = shipment.Id,
                Status = newStatus.Value,
                RawStatus = ghnRawStatus,
                OccurredAt = now.UtcDateTime,
                Source = source,
            });
            await database.SaveChangesAsync(CancellationToken.None);

            // Chỉ phát sự kiện khi trạng thái chuẩn đổi. Đổi trạng thái gốc trong cùng trạng thái chuẩn thì module khác không cần biết.
            if (statusChanged)
            {
                // Nguồn `Manual` là thao tác của shop, còn webhook và job đồng bộ là tin từ hãng vận chuyển.
                // Bên nhận cần phân biệt hai nguồn này để không đổi đơn hai lần khi nhân viên hủy vận đơn.
                var changeSource = source == ShipmentEventSource.Manual
                    ? ShipmentStatusChangeSource.Shop
                    : ShipmentStatusChangeSource.Carrier;

                // `Ref` trong sự kiện là mã gốc, vì bên nhận biết đơn theo mã đó chứ không biết hậu tố nội bộ.
                events.Emit(new ShipmentStatusChangedEvent(
                    shipment.Id, shipment.Ref, newStatus.Value, shipment.Status, now, changeSource));
            }

            await transaction.CommitAsync(CancellationToken.None);
        }

        // Phát sau commit. Bên nhận đọc DB bằng kết nối khác vẫn thấy trạng thái mới.
        await events.FlushAsync(CancellationToken.None);
        return statusChanged
            ? ShipmentStatusUpdateOutcome.StatusChanged
            : ShipmentStatusUpdateOutcome.RawStatusUpdated;
    }
}
