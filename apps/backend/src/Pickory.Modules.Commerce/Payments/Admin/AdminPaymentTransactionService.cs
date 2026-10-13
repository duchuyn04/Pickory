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
        var transactions = database.PaymentTransactions.AsNoTracking();

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

        // Đếm cùng điều kiện lọc. Giao diện biết có bao nhiêu trang.
        var total = await transactions.CountAsync(cancellationToken);

        // Mới nhất lên đầu, rồi theo `Id`. Thiếu `Id` thì phân trang lặp hoặc sót các giao dịch cùng thời điểm.
        var rows = await transactions
            .OrderByDescending(row => row.ReceivedAt)
            .ThenBy(row => row.Id)
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
