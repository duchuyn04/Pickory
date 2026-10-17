namespace Pickory.Modules.Commerce.Orders.Lookup;

// Chuẩn hóa mã đơn và email trước khi kiểm tra định dạng hoặc tra cứu.
public static class OrderLookupNormalizer
{
    // Cắt khoảng trắng hai đầu và chuyển sang chữ hoa để mã đơn thống nhất.
    public static string NormalizeOrderCode(string? orderCode)
    {
        // Chuỗi null hoặc rỗng trả chuỗi rỗng để bên gọi kiểm tra dễ dàng.
        return string.IsNullOrWhiteSpace(orderCode) ? string.Empty : orderCode.Trim().ToUpperInvariant();
    }

    // Cắt khoảng trắng hai đầu và chuyển sang chữ thường để email thống nhất.
    public static string NormalizeEmail(string? email)
    {
        // Chuỗi null hoặc rỗng trả chuỗi rỗng để bên gọi kiểm tra dễ dàng.
        return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
    }
}
