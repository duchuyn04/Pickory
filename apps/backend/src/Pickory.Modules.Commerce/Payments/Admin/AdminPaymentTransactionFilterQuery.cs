using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Payments.Admin;

// Câu truy vấn lọc giao dịch viết một lần. Route danh sách và route xuất file cùng gọi, nên hai route luôn thấy cùng tập giao dịch.
internal static class AdminPaymentTransactionFilterQuery
{
    // Chỉ áp các điều kiện lọc. Hàm không đọc `filter.Page`, nên route xuất bỏ qua phân trang mà không cần nhánh riêng.
    public static IQueryable<PaymentTransaction> Apply(
        IQueryable<PaymentTransaction> transactions, AdminPaymentTransactionListQuery filter)
    {
        // Mỗi bộ lọc chỉ thêm điều kiện khi có tham số. Các điều kiện kết hợp theo AND.
        if (filter.MatchStatus is { } matchStatus)
        {
            transactions = transactions.Where(row => row.MatchStatus == matchStatus);
        }

        // Giao dịch chưa gắn đơn có `Order` là null, nên điều kiện cờ loại chúng ra.
        if (filter.OrderFlag is { } orderFlag)
        {
            transactions = transactions.Where(row => row.Order != null && row.Order.Flags.Contains(orderFlag));
        }

        return transactions;
    }

    // Mới nhất lên đầu, rồi theo `Id`. Thiếu `Id` thì phân trang lặp hoặc sót các giao dịch cùng thời điểm.
    public static IOrderedQueryable<PaymentTransaction> NewestFirst(IQueryable<PaymentTransaction> transactions)
    {
        return transactions
            .OrderByDescending(row => row.ReceivedAt)
            .ThenBy(row => row.Id);
    }
}
