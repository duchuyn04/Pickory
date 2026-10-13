using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Dịch vụ tạo và hủy vận đơn GHN của một đơn, gọi GHN ngoài transaction để không giữ khóa dòng khi chờ mạng.
// Bảng `orders` không có cột vận đơn, nên vận đơn hiện tại là vận đơn của module Shipping có `Ref` bằng mã đơn.
public sealed class AdminOrderShipmentService(
    CommerceDbContext database,
    IShippingService shipping,
    OrderParcelResolver parcels,
    AdminOrderService orders,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher,
    TimeProvider clock,
    ILogger<AdminOrderShipmentService> logger)
{
    public const string CreateAuditAction = "commerce.order.shipment_create";
    public const string CancelAuditAction = "commerce.order.shipment_cancel";

    private const string OrderNotFoundMessage = "Không tìm thấy đơn hàng.";
    private const string InvalidTransitionCode = "INVALID_ORDER_TRANSITION";
    private const string CreateRejectedMessage = "Chỉ tạo được vận đơn cho đơn đã xác nhận.";
    private const string CancelRejectedMessage = "Chỉ hủy được vận đơn của đơn đang giao và còn vận đơn hiệu lực.";
    private const string OrderChangedMessage = "Đơn đã đổi trạng thái trong lúc tạo vận đơn, nên vận đơn vừa tạo đã bị hủy.";
    private const string OrderChangedCancelFailedMessage = "Đơn đã đổi trạng thái trong lúc tạo vận đơn. Hủy vận đơn vừa tạo thất bại, hãy kiểm tra với shipping.";
    private const string ShipmentCancelRaceMessage = "Đơn đã đổi trạng thái trong lúc hủy vận đơn.";
    private const string ParcelMissingCode = "PARCEL_DIMENSIONS_MISSING";
    private const string ParcelMissingMessage = "Sản phẩm trong đơn chưa có cân nặng hoặc kích thước. Hãy nhập kích thước kiện.";

    // Kết quả của bước ghi trạng thái đơn sau khi khóa dòng đơn.
    private enum MarkResult
    {
        Applied,
        AlreadyInTargetStatus,
        OrderChanged,
    }

    public async Task<AdminOrderDetailResponse> CreateAsync(
        string code, string actorId, Parcel? requestedParcel, CancellationToken cancellationToken)
    {
        // Đọc đơn không khóa trước. Lời gọi GHN bên dưới là lời gọi mạng nên không được giữ khóa dòng.
        var order = await database.Orders.AsNoTracking()
            .Include(candidate => candidate.Items)
            .SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);
        if (order is null)
        {
            throw new AppException("ORDER_NOT_FOUND", 404, OrderNotFoundMessage);
        }

        // Từ chối sớm để đơn chưa xác nhận hay đã hủy không tốn một lời gọi GHN. Kiểm lại sau khi khóa dòng.
        if (order.Status != OrderStatus.Confirmed)
        {
            throw new AppException(InvalidTransitionCode, 409, CreateRejectedMessage);
        }

        // Nhân viên nhập kiện thì dùng đúng kiện đó. Không nhập thì dựng từ số đo SKU như checkout.
        var parcel = requestedParcel ?? await parcels.BuildAsync(order.Items, cancellationToken);
        if (parcel is null)
        {
            throw new AppException(ParcelMissingCode, 422, ParcelMissingMessage);
        }

        // Shipping chống tạo trùng theo `Ref`, nên bấm lại sau lỗi mạng không tạo vận đơn thứ hai.
        // Lỗi `ShippingRejectedException` và `ShippingUnavailableException` đi thẳng ra ngoài và đơn giữ `confirmed`.
        var shipment = await shipping.CreateShipmentAsync(BuildCreateRequest(order, parcel), cancellationToken);

        var result = await MarkOrderShippingAsync(order.Id, actorId, shipment, cancellationToken);
        if (result == MarkResult.OrderChanged)
        {
            // Khóa dòng đơn đã nhả ở bước trên, nên lời gọi GHN dưới đây không giữ khóa.
            var cancelled = await TryCancelOrphanShipmentAsync(order.Code, shipment);
            throw new AppException(
                InvalidTransitionCode, 409, cancelled ? OrderChangedMessage : OrderChangedCancelFailedMessage);
        }

        return await orders.GetDetailAsync(code, cancellationToken);
    }

    public async Task<AdminOrderDetailResponse> CancelAsync(
        string code, string actorId, CancellationToken cancellationToken)
    {
        var order = await database.Orders.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);
        if (order is null)
        {
            throw new AppException("ORDER_NOT_FOUND", 404, OrderNotFoundMessage);
        }

        // Đơn phải đang giao và còn vận đơn hiệu lực. Thiếu một trong hai thì không có gì để hủy.
        var shipment = order.Status == OrderStatus.Shipping
            ? await shipping.GetShipmentByRefAsync(order.Code, cancellationToken)
            : null;
        if (shipment is null)
        {
            throw new AppException(InvalidTransitionCode, 409, CancelRejectedMessage);
        }

        // Hủy ở GHN trước khi đổi đơn. GHN từ chối hay lỗi mạng thì đơn giữ `shipping` và vận đơn còn nguyên.
        await shipping.CancelShipmentAsync(shipment.Id, cancellationToken);

        var result = await MarkOrderConfirmedAsync(order.Id, actorId, shipment, cancellationToken);
        if (result == MarkResult.OrderChanged)
        {
            throw new AppException(InvalidTransitionCode, 409, ShipmentCancelRaceMessage);
        }

        return await orders.GetDetailAsync(code, cancellationToken);
    }

    private async Task<MarkResult> MarkOrderShippingAsync(
        string orderId, string actorId, Shipment shipment, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        // Yêu cầu bấm trùng đã đưa đơn sang `shipping` với đúng vận đơn này, vì `Ref` có tối đa một vận đơn hiệu lực.
        // Trả thành công và không ghi thêm, để lịch sử và audit chỉ có một dòng.
        if (order.Status == OrderStatus.Shipping)
        {
            return MarkResult.AlreadyInTargetStatus;
        }

        // Đơn không còn `confirmed` (ví dụ nhân viên khác vừa hủy) thì không được chuyển sang `shipping`.
        var decision = OrderTransitions.Evaluate(order.Status, OrderStatus.Shipping, TransitionActor.Staff, hasActiveShipment: false);
        if (order.Status != OrderStatus.Confirmed || !decision.IsAllowed)
        {
            return MarkResult.OrderChanged;
        }

        var events = new DomainEventCollector(dispatcher);
        try
        {
            var previousStatus = order.Status;
            order.Status = OrderStatus.Shipping;

            // Cờ báo hãng vận chuyển đã hủy vận đơn trước. Có vận đơn mới thì việc đó đã xong, nên bỏ cờ.
            order.Flags.Remove(ShipmentStatusChangedHandler.ShipmentCancelledFlag);

            // Lý do ghi cả mã vận đơn nội bộ và mã GHN, vì đơn không có cột vận đơn để truy ngược.
            var reason = $"Tạo vận đơn {shipment.TrackingCode} (mã vận đơn nội bộ {shipment.Id}).";
            var now = OrderHistoryEntries.TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
            OrderHistoryEntries.Add(database, order, previousStatus, OrderStatus.Shipping, actorId, reason, now);

            // Gom sự kiện để phát sau commit. Bên nghe chỉ đọc dữ liệu đã lưu.
            events.Emit(new OrderStatusChangedEvent(order.Id, order.Code, previousStatus, OrderStatus.Shipping));

            await database.SaveChangesAsync(cancellationToken);

            // Mỗi lần tạo ghi một dòng audit trong cùng transaction, kèm mã vận đơn để truy vết.
            var entry = new AuditLogEntry(
                actorId,
                CreateAuditAction,
                OrderCancellationService.AuditTargetType,
                order.Code,
                new { status = OrderLabels.ToLabel(previousStatus) },
                new { status = OrderLabels.ToLabel(order.Status), shipmentId = shipment.Id, trackingCode = shipment.TrackingCode });
            await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            events.Discard();
            throw;
        }

        await events.FlushAsync(cancellationToken);
        return MarkResult.Applied;
    }

    private async Task<MarkResult> MarkOrderConfirmedAsync(
        string orderId, string actorId, Shipment shipment, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        // Yêu cầu hủy trùng đã đưa đơn về `confirmed`. Hủy vận đơn ở GHN không làm gì lần hai, nên trả thành công.
        if (order.Status == OrderStatus.Confirmed)
        {
            return MarkResult.AlreadyInTargetStatus;
        }

        // Bảng trạng thái cho nhân viên đưa `shipping` về `confirmed`. Trạng thái khác (đã giao, đã hoàn) thì từ chối.
        var decision = OrderTransitions.Evaluate(order.Status, OrderStatus.Confirmed, TransitionActor.Staff, hasActiveShipment: false);
        if (order.Status != OrderStatus.Shipping || !decision.IsAllowed)
        {
            return MarkResult.OrderChanged;
        }

        var events = new DomainEventCollector(dispatcher);
        try
        {
            var previousStatus = order.Status;
            order.Status = OrderStatus.Confirmed;

            var reason = $"Nhân viên hủy vận đơn {shipment.TrackingCode} (mã vận đơn nội bộ {shipment.Id}).";
            var now = OrderHistoryEntries.TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
            OrderHistoryEntries.Add(database, order, previousStatus, OrderStatus.Confirmed, actorId, reason, now);

            events.Emit(new OrderStatusChangedEvent(order.Id, order.Code, previousStatus, OrderStatus.Confirmed));

            await database.SaveChangesAsync(cancellationToken);

            // Audit lưu mã vận đơn ở phần `before`, vì sau khi hủy vận đơn không còn là vận đơn hiện tại của đơn.
            var entry = new AuditLogEntry(
                actorId,
                CancelAuditAction,
                OrderCancellationService.AuditTargetType,
                order.Code,
                new { status = OrderLabels.ToLabel(previousStatus), shipmentId = shipment.Id, trackingCode = shipment.TrackingCode },
                new { status = OrderLabels.ToLabel(order.Status) });
            await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            events.Discard();
            throw;
        }

        await events.FlushAsync(cancellationToken);
        return MarkResult.Applied;
    }

    // Hủy vận đơn vừa tạo để GHN không còn đơn mồ côi mà shop không biết. Trả `false` khi hủy thất bại.
    private async Task<bool> TryCancelOrphanShipmentAsync(string orderCode, Shipment shipment)
    {
        try
        {
            // Dùng `CancellationToken.None` vì client ngắt kết nối không được bỏ dở việc dọn vận đơn.
            await shipping.CancelShipmentAsync(shipment.Id, CancellationToken.None);
            return true;
        }
        catch (AppException failure)
        {
            // Vận đơn GHN còn sống mà đơn đã đổi. Ghi mức lỗi kèm mã để nhân viên dọn tay.
            logger.LogError(
                "Hủy vận đơn mồ côi thất bại với mã {ErrorCode}. Đơn {OrderCode}, mã vận đơn nội bộ {ShipmentId}, mã vận đơn GHN {TrackingCode}.",
                failure.Code,
                orderCode,
                shipment.Id,
                shipment.TrackingCode);
            return false;
        }
    }

    private static CreateShipmentRequest BuildCreateRequest(Order order, Parcel parcel)
    {
        var address = new ShippingAddress(
            ReadString(order.ShippingAddress, "provinceCode"),
            ReadString(order.ShippingAddress, "provinceName"),
            ReadString(order.ShippingAddress, "wardCode"),
            ReadString(order.ShippingAddress, "wardName"),
            ReadString(order.ShippingAddress, "line"));
        var recipient = new ShippingRecipient(
            ReadString(order.Contact, "name"), ReadString(order.Contact, "phone"), address);

        var lines = order.Items
            .OrderBy(item => item.NameSnapshot)
            .ThenBy(item => item.SkuId)
            .Select(item => new ShipmentLine(item.NameSnapshot, item.Quantity))
            .ToList();

        // Đơn SePay đã trả trước nên shipper không thu hộ. Đơn COD thu đúng tổng tiền khách phải trả.
        var codAmount = order.PaymentMethod == PaymentMethod.Cod ? order.Total : 0;

        // `Ref` là mã đơn để shipping chống trùng và để tra vận đơn hiện tại của đơn.
        // Giá trị khai giá bằng tạm tính, cùng cách checkout báo phí.
        return new CreateShipmentRequest(order.Code, recipient, parcel, lines, codAmount, order.Subtotal, Note: null);
    }

    // Bản chụp thiếu trường thì trả chuỗi rỗng. Shipping từ chối dữ liệu thiếu bằng lỗi nghiệp vụ rõ ràng.
    private static string ReadString(JsonDocument snapshot, string propertyName)
    {
        return snapshot.RootElement.ValueKind == JsonValueKind.Object
            && snapshot.RootElement.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }
}
