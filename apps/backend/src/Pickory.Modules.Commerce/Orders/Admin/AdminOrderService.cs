using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Shared.Calendar;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Đọc danh sách và chi tiết đơn cho nhân viên. Khác `OrderTrackingService`, dịch vụ này không giới hạn theo chủ đơn.
public sealed class AdminOrderService(
    CommerceDbContext database,
    ICatalogReader catalogReader,
    IShippingService shipping,
    OrderParcelResolver parcels)
{
    private const string OrderNotFoundMessage = "Không tìm thấy đơn hàng.";

    public async Task<PagedResponse<AdminOrderListItemResponse>> GetListAsync(
        AdminOrderListQuery filter,
        CancellationToken cancellationToken)
    {
        var orders = database.Orders.AsNoTracking();

        // Mỗi bộ lọc chỉ thêm điều kiện khi có tham số. Các điều kiện kết hợp theo AND.
        if (filter.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        if (filter.PaymentStatus is { } paymentStatus)
        {
            orders = orders.Where(order => order.PaymentStatus == paymentStatus);
        }

        if (filter.PaymentMethod is { } paymentMethod)
        {
            orders = orders.Where(order => order.PaymentMethod == paymentMethod);
        }

        if (filter.Flag is { } flag)
        {
            orders = orders.Where(order => order.Flags.Contains(flag));
        }

        // Ngày lọc theo giờ Việt Nam. Cận dưới gồm cận, cận trên không gồm cận để ngày được tính hết 23:59:59.999.
        if (filter.From is { } from)
        {
            var fromUtc = VietnamCalendar.DayStartUtc(from);
            orders = orders.Where(order => order.CreatedAt >= fromUtc);
        }

        if (filter.To is { } to)
        {
            var toExclusiveUtc = VietnamCalendar.DayEndExclusiveUtc(to);
            orders = orders.Where(order => order.CreatedAt < toExclusiveUtc);
        }

        if (filter.Search is { } term)
        {
            // Nhân viên có thể chỉ biết mã đơn, tên, số điện thoại hoặc email khi khách gọi tới. Điều kiện kết hợp theo OR.
            var pattern = term.LikePattern;

            // Đơn thiếu khóa JSON cho giá trị NULL trong SQL, và `ILIKE` với NULL không khớp. Dấu `!` chỉ để biên dịch chấp nhận kiểu.
            orders = orders.Where(order =>
                // Mã đơn và số điện thoại không có dấu, nên không cần `unaccent`. Mẫu đã escape `%` và `_` trong từ khóa.
                EF.Functions.ILike(order.Code, pattern, SearchTerm.EscapeCharacter)
                || EF.Functions.ILike(order.Contact.RootElement.GetProperty("phone").GetString()!, pattern, SearchTerm.EscapeCharacter)
                // Tên và email không dấu và không phân biệt hoa thường. Từ khóa đi qua `unaccent` như cột, để hai phía cùng dạng.
                || EF.Functions.ILike(
                    EF.Functions.Unaccent(order.Contact.RootElement.GetProperty("name").GetString()!),
                    EF.Functions.Unaccent(pattern),
                    SearchTerm.EscapeCharacter)
                || EF.Functions.ILike(
                    EF.Functions.Unaccent(order.Contact.RootElement.GetProperty("email").GetString()!),
                    EF.Functions.Unaccent(pattern),
                    SearchTerm.EscapeCharacter));
        }

        // Đếm cùng điều kiện lọc, gồm cả từ khóa. Giao diện biết có bao nhiêu trang.
        var total = await orders.CountAsync(cancellationToken);

        // Sắp xếp theo ngày tạo giảm dần rồi theo `Id`. Thiếu `Id` thì phân trang lặp hoặc sót các đơn cùng ngày tạo.
        var rows = await orders
            .OrderByDescending(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .Skip(filter.Page.Skip)
            .Take(filter.Page.Limit)
            // Chỉ chiếu các cột cần dùng. Số dòng hàng là truy vấn con COUNT nên không nạp dòng hàng và không N+1.
            .Select(order => new
            {
                order.Code,
                order.CreatedAt,
                order.Status,
                order.PaymentStatus,
                order.PaymentMethod,
                order.Total,
                order.Flags,
                order.Contact,
                LineCount = order.Items.Count,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new AdminOrderListItemResponse(
                row.Code,
                row.CreatedAt,
                OrderLabels.ToLabel(row.Status),
                OrderLabels.ToLabel(row.PaymentStatus),
                OrderLabels.ToLabel(row.PaymentMethod),
                row.Total,
                row.Flags,
                ReadContactField(row.Contact, "name"),
                ReadContactField(row.Contact, "phone"),
                row.LineCount))
            .ToList();

        return new PagedResponse<AdminOrderListItemResponse>(items, total, filter.Page.Page, filter.Page.Limit);
    }

    public async Task<AdminOrderDetailResponse> GetDetailAsync(string code, CancellationToken cancellationToken)
    {
        // Nhân viên đọc được mọi đơn, nên không kiểm chủ đơn hay token. Quyền `orders:read` đã kiểm ở route.
        var order = await database.Orders.AsNoTracking()
            .Include(candidate => candidate.Items)
            .Include(candidate => candidate.StatusHistory)
            .SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);
        if (order is null)
        {
            throw new AppException("ORDER_NOT_FOUND", 404, OrderNotFoundMessage);
        }

        // Bảng `orders` không có cột vận đơn. Tra module Shipping theo mã đơn, và nhận `null` khi chưa có vận đơn còn hiệu lực.
        var shipment = await shipping.GetShipmentByRefAsync(order.Code, cancellationToken);

        // Lịch sử theo thời gian tăng dần. Cùng thời điểm thì theo `Id` để thứ tự không đổi giữa các lần đọc.
        var history = order.StatusHistory
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .Select(entry => new AdminOrderStatusHistoryResponse(
                OrderLabels.ToLabel(entry.FromStatus),
                OrderLabels.ToLabel(entry.ToStatus),
                entry.ActorId,
                entry.Reason,
                entry.CreatedAt))
            .ToList();

        // Gợi ý kiện chỉ có nghĩa khi đơn sắp tạo vận đơn. Trạng thái khác không tốn truy vấn catalog.
        var suggestedParcel = order.Status == OrderStatus.Confirmed
            ? await parcels.BuildAsync(order.Items, cancellationToken)
            : null;

        return new AdminOrderDetailResponse(
            order.Code,
            OrderLabels.ToLabel(order.Status),
            OrderLabels.ToLabel(order.PaymentStatus),
            OrderLabels.ToLabel(order.PaymentMethod),
            order.CreatedAt,
            order.PaymentDeadline,
            order.Flags,
            order.Contact,
            order.ShippingAddress,
            order.InvoiceInfo,
            OrderItemMapper.ToResponses(order.Items, catalogReader),
            order.Subtotal,
            order.ShippingFee,
            order.ShippingDiscount,
            order.Total,
            OrderLabels.ToLabel(order.ShippingFeeSource),
            history,
            shipment is null
                ? null
                : new AdminOrderShipmentResponse(
                    shipment.Id, shipment.TrackingCode, OrderLabels.ToLabel(shipment.Status), shipment.Fee),
            suggestedParcel is null
                ? null
                : new AdminOrderParcelResponse(
                    suggestedParcel.WeightGrams, suggestedParcel.LengthCm, suggestedParcel.WidthCm, suggestedParcel.HeightCm),
            order.RefundTransactionRef);
    }

    // Tên và số điện thoại nằm trong bản chụp liên hệ. Bản chụp thiếu trường thì trả `null` thay vì ném lỗi.
    private static string? ReadContactField(JsonDocument contact, string propertyName)
    {
        return contact.RootElement.ValueKind == JsonValueKind.Object
            && contact.RootElement.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
