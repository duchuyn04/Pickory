using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Staff;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Đọc danh sách và chi tiết đơn cho nhân viên. Khác `OrderTrackingService`, dịch vụ này không giới hạn theo chủ đơn.
public sealed class AdminOrderService(
    CommerceDbContext database,
    ICatalogReader catalogReader,
    IShippingService shipping,
    OrderParcelResolver parcels,
    IStaffDirectory staffDirectory)
{
    private const string OrderNotFoundMessage = "Không tìm thấy đơn hàng.";

    public async Task<PagedResponse<AdminOrderListItemResponse>> GetListAsync(
        AdminOrderListQuery filter,
        CancellationToken cancellationToken)
    {
        // Câu lọc dùng chung với route xuất file. Hai route đổi bộ lọc cùng lúc, nên số dòng của file luôn khớp `total` của danh sách.
        var orders = AdminOrderFilterQuery.Apply(database.Orders.AsNoTracking(), filter);

        // Đếm cùng điều kiện lọc, gồm cả từ khóa. Giao diện biết có bao nhiêu trang.
        var total = await orders.CountAsync(cancellationToken);

        // Thứ tự dùng chung với route xuất file. Thiếu `Id` thì phân trang lặp hoặc sót các đơn cùng ngày tạo.
        var rows = await AdminOrderFilterQuery.NewestFirst(orders)
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

        // Gom mã người thực hiện để tra tên một lần, thay vì gọi Identity cho từng dòng lịch sử.
        var actorIds = order.StatusHistory
            .Where(entry => entry.ActorId is not null)
            .Select(entry => entry.ActorId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var actorNames = await staffDirectory.GetNamesAsync(actorIds, cancellationToken);

        // Lịch sử theo thời gian tăng dần. Cùng thời điểm thì theo `Id` để thứ tự không đổi giữa các lần đọc.
        var history = order.StatusHistory
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .Select(entry =>
            {
                // Tài khoản đã xóa hoặc không còn trong danh mục nhân viên thì trả tên rỗng; frontend tự có nhãn dự phòng.
                string? actorName = null;
                if (entry.ActorId is not null && actorNames.TryGetValue(entry.ActorId, out var name))
                {
                    actorName = name;
                }

                return new AdminOrderStatusHistoryResponse(
                    OrderLabels.ToLabel(entry.FromStatus),
                    OrderLabels.ToLabel(entry.ToStatus),
                    entry.ActorId,
                    actorName,
                    entry.Reason,
                    entry.CreatedAt);
            })
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
