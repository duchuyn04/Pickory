namespace Pickory.Modules.Commerce.Orders;

// Kết quả đặt đơn trả cho client, với số tiền là đồng Việt Nam và nhãn enum dạng snake_case.
public sealed record PlaceOrderResponse(
    string Code,
    string Status,
    string PaymentStatus,
    string PaymentMethod,
    int Subtotal,
    int ShippingFee,
    int ShippingDiscount,
    int Total,
    DateTime? PaymentDeadline,
    // Chỉ có ở lần tạo đơn. Lần gửi lại cùng khóa trả `null`, vì server chỉ giữ hash của token.
    string? AccessToken);

// `Created` là true khi request này tạo đơn mới và route trả 201. Gửi lại cùng khóa thì route trả 200.
public sealed record PlaceOrderResult(PlaceOrderResponse Order, bool Created);
