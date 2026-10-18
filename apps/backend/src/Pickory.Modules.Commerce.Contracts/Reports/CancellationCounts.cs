namespace Pickory.Modules.Commerce.Contracts.Reports;

// Số đơn hủy tạo trong ngày, tách theo lý do hủy.
public sealed record CancellationCounts(int ByCustomer, int ByShop, int PaymentExpired);
