using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Modules.Shipping.Shipments;

// Contracts cũng có kiểu `Shipment` cho bên gọi. Bộ đồng bộ làm việc với entity của module.
using Shipment = Pickory.Modules.Shipping.Entities.Shipment;

namespace Pickory.Modules.Shipping.Sync;

// Tra lại GHN cho mọi vận đơn chưa kết thúc để bù webhook bị mất. Cùng lớp áp trạng thái với webhook nên cùng luật chống trùng.
internal sealed class ShipmentStatusSyncProcessor(
    ShippingDbContext database,
    IGhnClient ghnClient,
    ShipmentStatusUpdater updater,
    ILogger<ShipmentStatusSyncProcessor> logger) : IShipmentStatusSyncProcessor
{
    // Danh sách trạng thái kết thúc lấy từ `IsTerminal`, nên job và định nghĩa chung của module không bao giờ lệch nhau.
    private static readonly ShipmentStatus[] TerminalStatuses =
        Enum.GetValues<ShipmentStatus>().Where(status => status.IsTerminal()).ToArray();

    public async Task<ShipmentStatusSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        // Vận đơn đã kết thúc không cần tra lại. Đọc không theo dõi vì lớp áp trạng thái ghi bằng câu lệnh riêng.
        var shipments = await database.Shipments
            .AsNoTracking()
            .Where(shipment => !TerminalStatuses.Contains(shipment.Status))
            // Vận đơn cũ nhất đứng trước, để vận đơn lâu chưa cập nhật được tra sớm nhất.
            .OrderBy(shipment => shipment.UpdatedAt)
            .ThenBy(shipment => shipment.Id)
            .ToListAsync(cancellationToken);

        var checkedCount = 0;
        var changedCount = 0;
        var failedCount = 0;
        foreach (var shipment in shipments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkedCount++;
            try
            {
                var outcome = await SyncShipmentAsync(shipment, cancellationToken);
                if (outcome is ShipmentStatusUpdateOutcome.StatusChanged or ShipmentStatusUpdateOutcome.RawStatusUpdated)
                {
                    changedCount++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Một vận đơn lỗi không được chặn cả vòng. Ghi mã vận đơn để người vận hành đối chiếu, rồi đi tiếp vận đơn sau.
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Đồng bộ trạng thái thất bại cho một vận đơn. Mã vận đơn GHN {TrackingCode}.",
                    shipment.TrackingCode);
            }
        }

        return new ShipmentStatusSyncResult(checkedCount, changedCount, failedCount);
    }

    private async Task<ShipmentStatusUpdateOutcome> SyncShipmentAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        // Mỗi vận đơn một lời gọi chi tiết. GHN là nguồn đúng, nên job không suy trạng thái từ dữ liệu đã lưu.
        var detail = await ghnClient.GetOrderDetailAsync(shipment.TrackingCode, cancellationToken);
        return await updater.ApplyAsync(shipment, detail.Status, ShipmentEventSource.Poll);
    }
}
