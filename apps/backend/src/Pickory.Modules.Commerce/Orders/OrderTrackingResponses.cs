using System.Text.Json;

namespace Pickory.Modules.Commerce.Orders;

// Dòng tóm tắt đơn hàng trong danh sách lịch sử đơn của khách hàng.
public sealed record OrderListItemResponse(
    string Code,
    string Status,
    string PaymentStatus,
    string PaymentMethod,
    int Total,
    DateTime CreatedAt,
    int ItemCount,
    string FirstItemName,
    string? FirstItemImagePublicId,
    string? FirstItemImageUrl);

// Chi tiết một dòng sản phẩm trong đơn hàng.
public sealed record OrderDetailItemResponse(
    string SkuId,
    string Name,
    string? VariantName,
    string? ImagePublicId,
    string? ImageUrl,
    int Quantity,
    int UnitPrice,
    int LineTotal);

// Dòng lịch sử thay đổi trạng thái của đơn hàng.
public sealed record OrderStatusHistoryResponse(
    string FromStatus,
    string ToStatus,
    DateTime CreatedAt);

// Dữ liệu chi tiết của một đơn hàng trả về cho khách xem hoặc hủy.
public sealed record OrderDetailResponse(
    string Code,
    string Status,
    string PaymentStatus,
    string PaymentMethod,
    DateTime CreatedAt,
    DateTime? PaymentDeadline,
    IReadOnlyList<OrderDetailItemResponse> Items,
    int Subtotal,
    int ShippingFee,
    int ShippingDiscount,
    int Total,
    JsonDocument Contact,
    JsonDocument ShippingAddress,
    JsonDocument? Invoice,
    IReadOnlyList<OrderStatusHistoryResponse> StatusHistory,
    bool CanCancel,
    string? TrackingCode);

// Dữ liệu trạng thái thanh toán của đơn phục vụ kiểm tra thanh toán.
public sealed record OrderPaymentResponse(
    string Code,
    string Status,
    string PaymentStatus,
    string PaymentMethod,
    int Total,
    DateTime? PaymentDeadline,
    string? BankAccountNumber,
    string? BankCode);
