using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Reports;

// Một đơn kèm thời điểm thanh toán suy ra từ dữ liệu có sẵn. `PaymentStatus` giữ lại để lớp gom tự quyết đơn nào tính doanh thu.
internal sealed record OrderPaymentRow(
    string OrderId,
    DateTime PaidAtUtc,
    int Total,
    PaymentStatus PaymentStatus,
    // Phương thức của dòng thanh toán để bộ đếm không lấy từ ngày tạo đơn.
    PaymentMethod PaymentMethod,
    // Số lượng sản phẩm bán ra của đơn để tính vào ngày ghi nhận doanh thu.
    int SoldQuantity = 0);
