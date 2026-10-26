using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Contracts.Reports;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Payments;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Calendar;

namespace Pickory.Modules.Commerce.Reports;

// Cổng số liệu của module Commerce cho `admin-console`. Mỗi hàm dùng số truy vấn cố định, không truy vấn theo từng đơn hay từng ngày.
public sealed class CommerceReports(CommerceDbContext database, ILogger<CommerceReports> logger) : ICommerceReports
{
    // Hàm theo ngày trả một dòng mỗi ngày và nạp dữ liệu cả khoảng vào bộ nhớ, nên khoảng bị chặn ở một năm nhuận.
    public const int MaxRangeDays = 366;

    // Giao dịch SePay đủ tiền: đã khớp đơn và `Amount` không nhỏ hơn tổng đơn.
    // Giao dịch chuyển thiếu cũng được gắn `OrderId` nhưng không làm đơn `paid`, nên không được tính là thời điểm thanh toán.
    private static readonly Expression<Func<PaymentTransaction, bool>> IsFullSepayPayment =
        transaction => transaction.MatchStatus == PaymentMatchStatuses.Matched
            && transaction.Order != null
            && transaction.Order.PaymentMethod == PaymentMethod.Sepay
            && transaction.Amount >= transaction.Order.Total;

    // Dòng lịch sử ghi lúc nhân viên đánh dấu đã nhận tiền COD. Đơn COD đã giao nên trạng thái đầu và cuối đều là `Delivered`.
    private static readonly Expression<Func<OrderStatusHistory, bool>> IsCodReceipt =
        history => history.Reason == OrderReportMarkers.CodReceivedReason
            && history.FromStatus == OrderStatus.Delivered
            && history.ToStatus == OrderStatus.Delivered
            && history.Order != null
            && history.Order.PaymentMethod == PaymentMethod.Cod;

    public async Task<IReadOnlyList<DailySales>> GetDailySalesAsync(DateRange range, CancellationToken cancellationToken)
    {
        // Kiểm khoảng ngày trước khi chạm DB để khoảng sai không tốn truy vấn nào.
        ValidateRange(range);
        var (startUtc, endExclusiveUtc) = ToUtcWindow(range);

        var payments = await LoadPaymentRowsAsync(startUtc, endExclusiveUtc, cancellationToken);
        var createdOrders = await LoadCreatedOrderRowsAsync(startUtc, endExclusiveUtc, cancellationToken);

        // Cảnh báo chỉ ở hàm theo ngày, vì đây là báo cáo chính của màn hình quản trị. Hàm theo sản phẩm không lặp lại cảnh báo.
        await WarnAboutPaidOrdersWithoutPaymentTimeAsync(startUtc, endExclusiveUtc, cancellationToken);

        // Chỉ đơn đủ điều kiện ghi nhận doanh thu mới cần tính số lượng sản phẩm bán ra.
        var recognizedOrderIds = payments
            .Where(payment => DailySalesAggregator.CountsAsRecognizedRevenue(payment.PaymentStatus))
            .Select(payment => payment.OrderId)
            .ToList();

        // Đọc số lượng sản phẩm của từng đơn để gộp vào ngày ghi nhận doanh thu.
        var quantitiesByOrderId = recognizedOrderIds.Count == 0
            ? new Dictionary<string, int>()
            : await database.OrderItems.AsNoTracking()
                .Where(item => recognizedOrderIds.Contains(item.OrderId))
                .GroupBy(item => item.OrderId)
                .Select(group => new { OrderId = group.Key, Quantity = group.Sum(item => item.Quantity) })
                .ToDictionaryAsync(item => item.OrderId, item => item.Quantity, cancellationToken);

        // Gắn số lượng vào từng dòng thanh toán trước khi chuyển sang lớp gom.
        var paymentsWithQuantities = payments
            .Select(payment => payment with
            {
                SoldQuantity = quantitiesByOrderId.GetValueOrDefault(payment.OrderId, 0),
            })
            .ToList();

        return DailySalesAggregator.Build(range, paymentsWithQuantities, createdOrders);
    }

    public async Task<IReadOnlyList<ProductSales>> GetProductSalesAsync(DateRange range, CancellationToken cancellationToken)
    {
        // Hàm này dùng cùng luật khoảng ngày với hàm theo ngày.
        ValidateRange(range);
        var (startUtc, endExclusiveUtc) = ToUtcWindow(range);

        // Chỉ đơn đủ điều kiện ghi nhận doanh thu mới góp dòng hàng. Luật này nằm ở lớp gom, nên hai hàm không lệch nhau.
        var payments = await LoadPaymentRowsAsync(startUtc, endExclusiveUtc, cancellationToken);
        var recognizedOrderIds = payments
            .Where(payment => DailySalesAggregator.CountsAsRecognizedRevenue(payment.PaymentStatus))
            .Select(payment => payment.OrderId)
            .ToList();

        // Không có đơn nào thì khỏi truy vấn dòng hàng.
        if (recognizedOrderIds.Count == 0)
        {
            return [];
        }

        // Gom trong SQL để chỉ đọc một dòng mỗi sản phẩm. Doanh thu là đơn giá nhân số lượng, nên không gồm phí ship.
        var rows = await database.OrderItems.AsNoTracking()
            .Where(item => recognizedOrderIds.Contains(item.OrderId))
            .GroupBy(item => item.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                // Cộng bằng `long` vì tổng nhiều dòng `int` có thể tràn.
                Revenue = group.Sum(item => (long)item.UnitPrice * item.Quantity),
                Quantity = group.Sum(item => item.Quantity),
            })
            .ToListAsync(cancellationToken);

        // Sắp xếp trong C# vì thứ tự `ProductId` phải là ordinal. Collation của Postgres sẽ xếp chữ thường trước chữ hoa.
        return rows
            .OrderByDescending(row => row.Revenue)
            .ThenByDescending(row => row.Quantity)
            .ThenBy(row => row.ProductId, StringComparer.Ordinal)
            .Select(row => new ProductSales(row.ProductId, row.Revenue, row.Quantity))
            .ToList();
    }

    public async Task<PendingCounts> GetPendingCountsAsync(CancellationToken cancellationToken)
    {
        var pendingConfirmation = await database.Orders.AsNoTracking()
            .CountAsync(order => order.Status == OrderStatus.PendingConfirmation, cancellationToken);

        // Giao dịch chưa khớp là tiền đã vào mà chưa gắn được đơn. Loại `matched` và `ignored` vì hai loại này không cần nhân viên đối soát.
        var unmatchedPayments = await database.PaymentTransactions.AsNoTracking()
            .CountAsync(transaction => transaction.MatchStatus == PaymentMatchStatuses.Unmatched, cancellationToken);

        // Đơn COD đã giao mà GHN chưa trả tiền về. Điều kiện `Unpaid` loại đơn đã nhận tiền, đang chờ hoàn hoặc đã hoàn.
        var codUnreceived = await database.Orders.AsNoTracking()
            .CountAsync(
                order => order.PaymentMethod == PaymentMethod.Cod
                    && order.Status == OrderStatus.Delivered
                    && order.PaymentStatus == PaymentStatus.Unpaid,
                cancellationToken);

        return new PendingCounts(pendingConfirmation, unmatchedPayments, codUnreceived);
    }

    private static void ValidateRange(DateRange range)
    {
        // Khoảng ngược không có đơn nào khớp. Đây là lỗi lập trình của bên gọi nên ném lỗi thay vì trả danh sách rỗng.
        if (range.From > range.To)
        {
            throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.", nameof(range));
        }

        // Cộng thêm một vì khoảng gồm cả hai đầu.
        var dayCount = range.To.DayNumber - range.From.DayNumber + 1;
        if (dayCount > MaxRangeDays)
        {
            throw new ArgumentException($"Khoảng ngày không được dài quá {MaxRangeDays} ngày.", nameof(range));
        }
    }

    private static (DateTime StartUtc, DateTime EndExclusiveUtc) ToUtcWindow(DateRange range)
    {
        // Cận dưới gồm cận và cận trên không gồm cận, để hai ngày liền nhau không bỏ sót và không trùng một khoảnh khắc nào.
        return (VietnamCalendar.DayStartUtc(range.From), VietnamCalendar.DayEndExclusiveUtc(range.To));
    }

    // Đọc thời điểm thanh toán của các đơn có thời điểm này nằm trong khoảng. Lớp gom quyết định đơn nào tính doanh thu.
    private async Task<IReadOnlyList<OrderPaymentRow>> LoadPaymentRowsAsync(
        DateTime startUtc, DateTime endExclusiveUtc, CancellationToken cancellationToken)
    {
        // Nhóm theo đơn rồi lấy thời điểm sớm nhất, vì một đơn có thể có nhiều giao dịch đủ tiền.
        // Lọc khoảng sau khi nhóm để một giao dịch trả thừa muộn hơn không kéo đơn sang ngày khác.
        var sepayRows = await database.PaymentTransactions.AsNoTracking()
            .Where(IsFullSepayPayment)
            .GroupBy(transaction => new
            {
                transaction.OrderId,
                transaction.Order!.Total,
                transaction.Order.PaymentStatus,
            })
            .Select(group => new
            {
                OrderId = group.Key.OrderId!,
                group.Key.Total,
                group.Key.PaymentStatus,
                PaidAtUtc = group.Min(transaction => transaction.ReceivedAt),
            })
            .Where(row => row.PaidAtUtc >= startUtc && row.PaidAtUtc < endExclusiveUtc)
            .ToListAsync(cancellationToken);

        // Đơn COD lấy thời điểm nhân viên đánh dấu nhận tiền. Cũng nhóm theo đơn để lần đánh dấu sớm nhất là ngày thanh toán.
        var codRows = await database.OrderStatusHistory.AsNoTracking()
            .Where(IsCodReceipt)
            .GroupBy(history => new
            {
                history.OrderId,
                history.Order!.Total,
                history.Order.PaymentStatus,
            })
            .Select(group => new
            {
                group.Key.OrderId,
                group.Key.Total,
                group.Key.PaymentStatus,
                PaidAtUtc = group.Min(history => history.CreatedAt),
            })
            .Where(row => row.PaidAtUtc >= startUtc && row.PaidAtUtc < endExclusiveUtc)
            .ToListAsync(cancellationToken);

        return sepayRows
            .Select(row => new OrderPaymentRow(row.OrderId, row.PaidAtUtc, row.Total, row.PaymentStatus))
            .Concat(codRows.Select(row => new OrderPaymentRow(row.OrderId, row.PaidAtUtc, row.Total, row.PaymentStatus)))
            .ToList();
    }

    private async Task WarnAboutPaidOrdersWithoutPaymentTimeAsync(
        DateTime startUtc, DateTime endExclusiveUtc, CancellationToken cancellationToken)
    {
        // Đơn `Paid` không có giao dịch đủ tiền và không có dòng nhận COD thì không có ngày để xếp doanh thu.
        // Chỉ xét đơn tạo trong khoảng, nên mỗi đơn lệch chỉ được báo ở khoảng chứa ngày tạo.
        var missingCount = await database.Orders.AsNoTracking()
            .Where(order => order.PaymentStatus == PaymentStatus.Paid
                && order.CreatedAt >= startUtc
                && order.CreatedAt < endExclusiveUtc
                && !database.PaymentTransactions.Where(IsFullSepayPayment).Any(transaction => transaction.OrderId == order.Id)
                && !database.OrderStatusHistory.Where(IsCodReceipt).Any(history => history.OrderId == order.Id))
            .CountAsync(cancellationToken);

        // Chỉ cảnh báo, không ném lỗi, để một đơn dữ liệu lệch không làm hỏng cả báo cáo. Log không ghi mã đơn hay thông tin khách.
        if (missingCount > 0)
        {
            logger.LogWarning(
                "Có {MissingCount} đơn đã thanh toán nhưng không tìm được thời điểm thanh toán, nên không tính vào doanh thu.",
                missingCount);
        }
    }

    // Chiếu đơn tạo trong khoảng sang dòng nhỏ. Lý do hủy mới nhất lấy bằng truy vấn con, nên không truy vấn theo từng đơn.
    private async Task<IReadOnlyList<CreatedOrderRow>> LoadCreatedOrderRowsAsync(
        DateTime startUtc, DateTime endExclusiveUtc, CancellationToken cancellationToken)
    {
        return await database.Orders.AsNoTracking()
            .Where(order => order.CreatedAt >= startUtc && order.CreatedAt < endExclusiveUtc)
            .Select(order => new CreatedOrderRow(
                order.CreatedAt,
                order.Status,
                order.PaymentMethod,
                order.Total,
                order.ShippingDiscount,
                order.Flags.Contains(OrderReportMarkers.PaymentExpiredFlag),
                order.Status == OrderStatus.Cancelled
                    ? order.StatusHistory
                        .Where(history => history.ToStatus == OrderStatus.Cancelled)
                        .OrderByDescending(history => history.CreatedAt)
                        .ThenByDescending(history => history.Id)
                        .Select(history => history.Reason)
                        .FirstOrDefault()
                    : null))
            .ToListAsync(cancellationToken);
    }
}
