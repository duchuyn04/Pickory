using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;
using Pickory.Shared.Json;

namespace Pickory.Modules.Commerce.Orders;

// Dịch vụ theo dõi danh sách, chi tiết và thực hiện tự hủy đơn hàng cho khách.
public sealed class OrderTrackingService(
    CommerceDbContext database,
    ICatalogReader catalogReader,
    IStockService stock,
    IEventDispatcher dispatcher,
    TimeProvider clock)
{
    private const string OrderNotFoundMessage = "Không tìm thấy đơn hàng.";
    private const string InvalidTransitionMessage = "Không thể hủy đơn ở trạng thái này.";

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

        // Mở transaction để đảm bảo hoàn trả tồn kho và đổi trạng thái cùng thành công.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Khóa dòng đơn bằng SELECT FOR UPDATE để tuần tự hóa hai yêu cầu hủy cùng lúc.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM orders WHERE "id" = {initial.Id} FOR UPDATE""", cancellationToken);

        // Đọc lại đơn sau khi đã giữ khóa dòng để có trạng thái mới nhất.
        var order = await database.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .SingleAsync(o => o.Id == initial.Id, cancellationToken);

        // Đánh giá quyền tự hủy của khách theo máy trạng thái đơn hàng.
        var decision = OrderTransitions.Evaluate(order.Status, OrderStatus.Cancelled, TransitionActor.Customer, hasActiveShipment: false);
        if (!decision.IsAllowed)
        {
            throw new AppException("INVALID_ORDER_TRANSITION", 409, InvalidTransitionMessage);
        }

        var events = new DomainEventCollector(dispatcher);
        try
        {
            // Trả lại hàng đã giữ cho kho trong cùng transaction của đơn.
            await stock.ReleaseAsync(
                order.Id,
                new StockCallerTransaction(transaction.GetDbTransaction(), events),
                cancellationToken);

            var previousStatus = order.Status;

            // Đổi trạng thái đơn sang đã hủy và cập nhật trạng thái thanh toán tương ứng.
            order.Status = OrderStatus.Cancelled;
            order.PaymentStatus = OrderTransitions.PaymentStatusAfterOrderChange(OrderStatus.Cancelled, order.PaymentStatus);

            // Cắt về microsecond để thời điểm trong phản hồi khớp chính xác với giá trị đã lưu.
            var now = TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);

            // Ghi nhận một dòng lịch sử với lý do khách tự hủy.
            var history = new OrderStatusHistory
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = order.Id,
                FromStatus = previousStatus,
                ToStatus = OrderStatus.Cancelled,
                ActorId = callerUserId,
                Reason = "Khách tự hủy.",
                CreatedAt = now,
            };
            order.StatusHistory.Add(history);
            database.OrderStatusHistory.Add(history);

            // Gom sự kiện thay đổi trạng thái đơn để phát sau khi commit.
            events.Emit(new OrderStatusChangedEvent(order.Id, order.Code, previousStatus, OrderStatus.Cancelled));

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Hủy toàn bộ sự kiện đã gom khi transaction gặp lỗi.
            events.Discard();
            throw;
        }

        // Phát các sự kiện miền ra bên ngoài sau khi transaction đã commit thành công.
        await events.FlushAsync(cancellationToken);

        return ToDetailResponse(order);
    }

    private OrderDetailResponse ToDetailResponse(Order order)
    {
        // Tính toán cờ cho phép hủy để giao diện hiển thị nút hành động.
        var canCancel = OrderTransitions.Evaluate(
            order.Status, OrderStatus.Cancelled, TransitionActor.Customer, hasActiveShipment: false).IsAllowed;

        // Sắp xếp các dòng hàng theo tên và mã SKU để thứ tự hiển thị luôn xác định.
        var items = order.Items
            .OrderBy(item => item.NameSnapshot)
            .ThenBy(item => item.SkuId)
            .Select(item => new OrderDetailItemResponse(
                item.SkuId,
                item.NameSnapshot,
                FormatVariantName(item.OptionValues),
                item.ImagePublicId,
                ResolveImageUrl(item.ImagePublicId),
                item.Quantity,
                item.UnitPrice,
                checked(item.UnitPrice * item.Quantity)
            )).ToList();

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

    private static string? FormatVariantName(JsonDocument? optionValues)
    {
        // Bỏ qua định dạng khi dòng đơn không có tùy chọn biến thể.
        if (optionValues is null)
        {
            return null;
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                optionValues.RootElement.GetRawText(), PickoryJsonDefaults.Options);

            // Nối các giá trị biến thể thành chuỗi ngăn cách bởi dấu phẩy.
            return dict is not null && dict.Count > 0 ? string.Join(", ", dict.Values) : null;
        }
        catch (JsonException)
        {
            // Trả null khi tài liệu JSON không đúng cấu trúc từ điển.
            return null;
        }
    }

    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);

    // Dựng URL ảnh sản phẩm qua catalog khi có mã ảnh trong bản chụp.
    private string? ResolveImageUrl(string? publicId) =>
        string.IsNullOrEmpty(publicId) ? null : catalogReader.BuildProductImageUrl(publicId);
}
