namespace Pickory.Modules.Commerce.Orders.Lookup;

// Phản hồi của route tra cứu đơn. Mọi request hợp lệ đều nhận cùng thông điệp.
public sealed record OrderLookupResponse(string Message);
