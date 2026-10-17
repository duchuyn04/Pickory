namespace Pickory.Modules.Commerce.Orders.Lookup;

// Dữ liệu tra cứu đơn sau khi kiểm tra định dạng và chuẩn hóa.
public sealed record OrderLookupInput(string OrderCode, string Email);
