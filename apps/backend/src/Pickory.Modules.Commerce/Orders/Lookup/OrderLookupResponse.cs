namespace Pickory.Modules.Commerce.Orders.Lookup;

// Phản hồi thành công của route tra cứu. Token chỉ trả một lần để trình duyệt mở đơn ngay.
public sealed record OrderLookupResponse(string Code, string AccessToken);
