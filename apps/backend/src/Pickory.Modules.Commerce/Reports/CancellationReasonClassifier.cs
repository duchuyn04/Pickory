using Pickory.Modules.Commerce.Orders;

namespace Pickory.Modules.Commerce.Reports;

// Luật phân loại lý do hủy của một đơn `Cancelled`. Không chạm DB để unit test kiểm được từng nhánh.
internal static class CancellationReasonClassifier
{
    public static CancellationReason Classify(bool hasPaymentExpiredFlag, string? latestCancellationHistoryReason)
    {
        // Xét cờ trước vì cờ do hệ thống ghi. Lý do lịch sử của nhân viên là văn bản tự do và có thể trùng chữ với lý do của khách.
        if (hasPaymentExpiredFlag)
        {
            return CancellationReason.PaymentExpired;
        }

        // So sánh nguyên văn với hằng mà dịch vụ theo dõi đơn ghi, để code ghi và code báo cáo không lệch nhau.
        if (string.Equals(latestCancellationHistoryReason, OrderReportMarkers.CustomerCancellationReason, StringComparison.Ordinal))
        {
            return CancellationReason.Customer;
        }

        // Mọi đơn hủy còn lại, kể cả đơn không có dòng lịch sử hủy, được tính là shop hủy.
        return CancellationReason.Shop;
    }
}
