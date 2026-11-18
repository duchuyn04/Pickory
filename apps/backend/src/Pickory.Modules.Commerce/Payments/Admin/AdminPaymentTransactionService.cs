using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Payments.Admin;

// Đọc danh sách giao dịch SePay cho nhân viên đối soát.
public sealed class AdminPaymentTransactionService(CommerceDbContext database)
{
    public async Task<PagedResponse<AdminPaymentTransactionResponse>> GetListAsync(
        AdminPaymentTransactionListQuery filter, CancellationToken cancellationToken)
    {
        // Bộ lọc viết một lần trong truy vấn chung. Route xuất file dùng cùng điều kiện nên thấy cùng tập giao dịch.
        var transactions = AdminPaymentTransactionFilterQuery.Apply(database.PaymentTransactions.AsNoTracking(), filter);

        // Đếm cùng điều kiện lọc. Giao diện biết có bao nhiêu trang.
        var total = await transactions.CountAsync(cancellationToken);

        // Mới nhất lên đầu, rồi theo `Id`. Thứ tự này viết một lần trong truy vấn chung, để route xuất file dùng cùng thứ tự.
        var rows = await AdminPaymentTransactionFilterQuery.NewestFirst(transactions)
            .Skip(filter.Page.Skip)
            .Take(filter.Page.Limit)
            // Chỉ chiếu cột cần dùng. Cột `raw` không bao giờ rời DB.
            .Select(row => new
            {
                row.Id,
                row.ProviderTxId,
                row.Amount,
                row.Content,
                row.MatchStatus,
                row.ReceivedAt,
                OrderCode = row.Order != null ? row.Order.Code : null,
                OrderStatus = row.Order != null ? (OrderStatus?)row.Order.Status : null,
                OrderPaymentStatus = row.Order != null ? (PaymentStatus?)row.Order.PaymentStatus : null,
                OrderTotal = row.Order != null ? (int?)row.Order.Total : null,
                OrderFlags = row.Order != null ? row.Order.Flags : null,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new AdminPaymentTransactionResponse(
                row.Id,
                row.ProviderTxId,
                row.Amount,
                row.Content,
                row.MatchStatus,
                row.ReceivedAt,
                row.OrderCode is null
                    ? null
                    : new AdminPaymentOrderSummaryResponse(
                        row.OrderCode,
                        OrderLabels.ToLabel(row.OrderStatus!.Value),
                        OrderLabels.ToLabel(row.OrderPaymentStatus!.Value),
                        row.OrderTotal!.Value,
                        row.OrderFlags!)))
            .ToList();

        return new PagedResponse<AdminPaymentTransactionResponse>(items, total, filter.Page.Page, filter.Page.Limit);
    }
}
