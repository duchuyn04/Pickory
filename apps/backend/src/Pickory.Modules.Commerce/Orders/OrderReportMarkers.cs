namespace Pickory.Modules.Commerce.Orders;

// Chuỗi đánh dấu mà code ghi đơn và code báo cáo cùng tham chiếu.
// Giá trị đã nằm trong dữ liệu cũ của DB, nên không được đổi.
internal static class OrderReportMarkers
{
    // Lý do lịch sử khi khách tự hủy đơn qua route của khách.
    public const string CustomerCancellationReason = "Khách tự hủy.";

    // Lý do lịch sử khi nhân viên đánh dấu đã nhận tiền COD từ GHN.
    public const string CodReceivedReason = "Đã nhận tiền COD từ GHN.";

    // Cờ của đơn bị hủy vì quá hạn thanh toán. Cờ bị bỏ khi đơn được khôi phục sau khi khách trả muộn.
    public const string PaymentExpiredFlag = "payment_expired";
}
