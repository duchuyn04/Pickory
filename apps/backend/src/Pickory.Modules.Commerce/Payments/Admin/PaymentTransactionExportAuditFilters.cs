namespace Pickory.Modules.Commerce.Payments.Admin;

// Bộ lọc của một lần xuất, ghi vào nhật ký thao tác. Khóa là tên tham số của query, giá trị đúng như người dùng có thể gửi lại.
internal static class PaymentTransactionExportAuditFilters
{
    // Chỉ giữ tham số có giá trị. `page` và `limit` không có mặt, vì route xuất bỏ qua hai tham số này.
    public static IReadOnlyDictionary<string, string> From(AdminPaymentTransactionListQuery filter)
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);

        if (filter.MatchStatus is { } matchStatus)
        {
            filters[AdminPaymentTransactionListQuery.MatchStatusParameter] = matchStatus;
        }

        if (filter.OrderFlag is { } orderFlag)
        {
            filters[AdminPaymentTransactionListQuery.OrderFlagParameter] = orderFlag;
        }

        return filters;
    }
}
