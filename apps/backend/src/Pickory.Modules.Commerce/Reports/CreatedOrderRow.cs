using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Reports;

// Dòng đơn đã chiếu từ DB, đủ để lớp gom tính giá trị đơn đặt và số đơn hủy mà không cần entity.
// `LatestCancellationHistoryReason` là lý do của dòng lịch sử mới nhất có `ToStatus` là `Cancelled`, hoặc `null` nếu không có.
internal sealed record CreatedOrderRow(
    DateTime CreatedAtUtc,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    int Total,
    int ShippingDiscount,
    bool HasPaymentExpiredFlag,
    string? LatestCancellationHistoryReason);
