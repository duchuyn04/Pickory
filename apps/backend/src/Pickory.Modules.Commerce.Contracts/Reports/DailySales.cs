namespace Pickory.Modules.Commerce.Contracts.Reports;

// Số liệu bán hàng của một ngày lịch giờ Việt Nam. Số tiền là đồng Việt Nam kiểu `long`.
public sealed record DailySales(
    DateOnly Day,
    // Tổng `Total` của đơn `Paid`, xếp theo ngày thanh toán.
    long RecognizedRevenue,
    int PaidOrderCount,
    // Tổng `Total` của đơn tạo trong ngày, trừ đơn hủy vì quá hạn thanh toán.
    long PlacedOrderValue,
    // Mẫu số của tỉ lệ hủy. Phép chia do bên dùng làm.
    int CreatedOrderCount,
    CancellationCounts Cancellations,
    PaymentMethodCounts PaymentMethods,
    long ShippingDiscount,
    // Tổng số lượng sản phẩm của các đơn có doanh thu ghi nhận trong ngày.
    int SoldQuantity);
