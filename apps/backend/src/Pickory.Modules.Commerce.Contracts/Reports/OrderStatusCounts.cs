namespace Pickory.Modules.Commerce.Contracts.Reports;

// Số đơn theo trạng thái hiện tại của các đơn tạo trong kỳ.
public sealed record OrderStatusCounts(
    int PendingPayment,
    int PendingConfirmation,
    int Confirmed,
    int Shipping,
    int Delivered,
    int Returned,
    int Cancelled);
