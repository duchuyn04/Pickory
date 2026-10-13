using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders;

// Dịch vụ theo dõi danh sách, chi tiết và thực hiện tự hủy đơn hàng cho khách.
public sealed class OrderTrackingService(
    CommerceDbContext database,
    ICatalogReader catalogReader,
    OrderCancellationService cancellation)
{
    private const string OrderNotFoundMessage = "Không tìm thấy đơn hàng.";
    private const string CustomerCancellationReason = "Khách tự hủy.";

    public async Task<PagedResponse<OrderListItemResponse>> GetListAsync(
        string userId,
        PageQuery page,
        CancellationToken cancellationToken)
    {
        // Lọc danh sách theo tài khoản người gọi và xếp đơn mới nhất lên đầu.
        var query = database.Orders.AsNoTracking()
            .Where(order => order.UserId == userId)
            .OrderByDescending(order => order.CreatedAt);

        // Đếm tổng số đơn của tài khoản để phân trang trên giao diện.
        var total = await query.CountAsync(cancellationToken);

        // Nạp danh sách đơn theo trang kèm các dòng sản phẩm để tính tóm tắt.
        var orders = await query
            .Skip(page.Skip)
            .Take(page.Limit)
            .Include(order => order.Items)
            .ToListAsync(cancellationToken);

        // Chuyển đổi dữ liệu đơn sang dạng hiển thị tóm tắt không chứa dữ liệu cá nhân.
        var items = orders.Select(order =>
        {
            // Sắp xếp các dòng hàng để tên sản phẩm đầu tiên luôn ổn định giữa các lần đọc.
            var firstItem = order.Items.OrderBy(item => item.NameSnapshot).ThenBy(item => item.SkuId).FirstOrDefault();
            return new OrderListItemResponse(
                order.Code,
                OrderLabels.ToLabel(order.Status),
                OrderLabels.ToLabel(order.PaymentStatus),
                OrderLabels.ToLabel(order.PaymentMethod),
                order.Total,
                order.CreatedAt,
                order.Items.Sum(item => item.Quantity),
                firstItem?.NameSnapshot ?? string.Empty,
                firstItem?.ImagePublicId,
                ResolveImageUrl(firstItem?.ImagePublicId));
        }).ToList();

        return new PagedResponse<OrderListItemResponse>(items, total, page.Page, page.Limit);
    }

    public async Task<OrderDetailResponse> GetDetailAsync(
        string code,
        string? callerUserId,
        string? token,
        CancellationToken cancellationToken)
    {
        // Nạp thông tin đơn kèm sản phẩm và lịch sử thay đổi trạng thái.
        var order = await database.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .SingleOrDefaultAsync(o => o.Code == code, cancellationToken);

        // Trả lỗi 404 khi không tìm thấy hoặc người gọi không có quyền xem đơn.
        if (order is null || !OrderAccessPolicy.CanAccess(order.UserId, order.AccessTokenHash, callerUserId, token))
        {
            throw new AppException("ORDER_NOT_FOUND", 404, OrderNotFoundMessage);
        }

        return ToDetailResponse(order);
    }

    public async Task<OrderPaymentResponse> GetPaymentAsync(
        string code,
        string? callerUserId,
        string? token,
        CancellationToken cancellationToken)
    {
        // Tra cứu đơn để kiểm tra trạng thái thanh toán và hạn nộp tiền.
        var order = await database.Orders.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Code == code, cancellationToken);

        // Trả lỗi 404 bảo mật khi không tìm thấy đơn hoặc token không khớp.
        if (order is null || !OrderAccessPolicy.CanAccess(order.UserId, order.AccessTokenHash, callerUserId, token))
        {
            throw new AppException("ORDER_NOT_FOUND", 404, OrderNotFoundMessage);
        }

        // Chỉ đơn SePay còn chờ tiền được nhận thông tin ngân hàng; đơn khác không được mời chuyển khoản.
        string? bankAccountNumber = null;
        string? bankCode = null;
        if (order.PaymentMethod == PaymentMethod.Sepay &&
            order.Status == OrderStatus.PendingPayment &&
            order.PaymentStatus == PaymentStatus.Unpaid)
        {
            // Thiếu một giá trị cấu hình thì ẩn cả cặp để khách không chuyển tiền vào thông tin không đầy đủ.
            var settings = await database.Settings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(settings?.SepayAccountNumber) &&
                !string.IsNullOrWhiteSpace(settings.SepayBankCode))
            {
                bankAccountNumber = settings.SepayAccountNumber;
                bankCode = settings.SepayBankCode;
            }
        }

        return new OrderPaymentResponse(
            order.Code,
            OrderLabels.ToLabel(order.Status),
            OrderLabels.ToLabel(order.PaymentStatus),
            OrderLabels.ToLabel(order.PaymentMethod),
            order.Total,
            order.PaymentDeadline,
            bankAccountNumber,
            bankCode);
    }

    public async Task<OrderDetailResponse> CancelAsync(
        string code,
        string? callerUserId,
        string? token,
        CancellationToken cancellationToken)
    {
        // Kiểm tra quyền xem đơn trước khi mở transaction để chặn request trái phép.
        var initial = await database.Orders.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Code == code, cancellationToken);
        if (initial is null || !OrderAccessPolicy.CanAccess(initial.UserId, initial.AccessTokenHash, callerUserId, token))
        {
            throw new AppException("ORDER_NOT_FOUND", 404, OrderNotFoundMessage);
        }

        // Phần hủy dùng chung với nhân viên: khóa đơn, trả kho, đổi trạng thái, ghi lịch sử và phát sự kiện.
        var order = await cancellation.CancelAsync(
            initial.Id,
            new OrderCancellationRequest(TransitionActor.Customer, callerUserId, CustomerCancellationReason),
            cancellationToken);

        return ToDetailResponse(order);
    }

    private OrderDetailResponse ToDetailResponse(Order order)
    {
        // Tính toán cờ cho phép hủy để giao diện hiển thị nút hành động.
        var canCancel = OrderTransitions.Evaluate(
            order.Status, OrderStatus.Cancelled, TransitionActor.Customer, hasActiveShipment: false).IsAllowed;

        // Dòng hàng dùng chung bộ đổi với trang nhân viên để hai bên luôn thấy cùng một bản chụp.
        var items = OrderItemMapper.ToResponses(order.Items, catalogReader);

        // Sắp xếp lịch sử chuyển trạng thái theo thứ tự thời gian tăng dần.
        var statusHistory = order.StatusHistory
            .OrderBy(h => h.CreatedAt)
            .Select(h => new OrderStatusHistoryResponse(
                OrderLabels.ToLabel(h.FromStatus),
                OrderLabels.ToLabel(h.ToStatus),
                h.CreatedAt))
            .ToList();

        return new OrderDetailResponse(
            order.Code,
            OrderLabels.ToLabel(order.Status),
            OrderLabels.ToLabel(order.PaymentStatus),
            OrderLabels.ToLabel(order.PaymentMethod),
            order.CreatedAt,
            order.PaymentDeadline,
            items,
            order.Subtotal,
            order.ShippingFee,
            order.ShippingDiscount,
            order.Total,
            order.Contact,
            order.ShippingAddress,
            order.InvoiceInfo,
            statusHistory,
            canCancel,
            TrackingCode: null);
    }

    // Dựng URL ảnh sản phẩm qua catalog khi có mã ảnh trong bản chụp.
    private string? ResolveImageUrl(string? publicId) =>
        OrderItemMapper.ResolveImageUrl(catalogReader, publicId);
}
