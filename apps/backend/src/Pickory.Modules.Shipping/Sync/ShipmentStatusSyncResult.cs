namespace Pickory.Modules.Shipping.Sync;

// Kết quả một vòng đồng bộ trạng thái. `Checked` là số vận đơn đã tra GHN, `Changed` là số vận đơn được ghi trạng thái mới, `Failed` là số vận đơn lỗi.
internal sealed record ShipmentStatusSyncResult(int Checked, int Changed, int Failed);
