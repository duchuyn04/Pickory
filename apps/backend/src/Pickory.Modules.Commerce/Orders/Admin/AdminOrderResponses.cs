using System.Text.Json;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Dòng tóm tắt một đơn trong danh sách của nhân viên. `LineCount` là số dòng hàng, không phải tổng số lượng.
public sealed record AdminOrderListItemResponse(
    string Code,
    DateTime CreatedAt,
    string Status,
    string PaymentStatus,
    string PaymentMethod,
    int Total,
    IReadOnlyList<string> Flags,
    string? RecipientName,
    string? RecipientPhone,
    int LineCount);

// Một lần đổi trạng thái, kèm người thực hiện và lý do để nhân viên biết đơn đã qua tay ai.
// `ActorId` là `null` khi hệ thống hoặc khách vãng lai chuyển.
public sealed record AdminOrderStatusHistoryResponse(
    string FromStatus,
    string ToStatus,
    string? ActorId,
    string? Reason,
    DateTime CreatedAt);

// Vận đơn của đơn, lấy từ module Shipping theo mã đơn.
public sealed record AdminOrderShipmentResponse(
    string Id,
    string TrackingCode,
    string Status,
    int Fee);

// Chi tiết một đơn cho nhân viên, gồm bản chụp liên hệ, địa chỉ và hóa đơn lúc đặt.
// `Invoice` là `null` khi không có hóa đơn VAT, và `Shipment` là `null` khi chưa có vận đơn.
public sealed record AdminOrderDetailResponse(
    string Code,
    string Status,
    string PaymentStatus,
    string PaymentMethod,
    DateTime CreatedAt,
    DateTime? PaymentDeadline,
    IReadOnlyList<string> Flags,
    JsonDocument Contact,
    JsonDocument ShippingAddress,
    JsonDocument? Invoice,
    IReadOnlyList<OrderDetailItemResponse> Items,
    int Subtotal,
    int ShippingFee,
    int ShippingDiscount,
    int Total,
    IReadOnlyList<AdminOrderStatusHistoryResponse> StatusHistory,
    AdminOrderShipmentResponse? Shipment);
