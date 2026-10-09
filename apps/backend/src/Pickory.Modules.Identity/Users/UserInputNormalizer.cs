namespace Pickory.Modules.Identity.Users;

// Chuẩn hóa dữ liệu người dùng nhập trước khi lưu hoặc tra cứu.
public static class UserInputNormalizer
{
    // `Abc@x.com` và `abc@x.com` là cùng một tài khoản. Mọi chỗ tra cứu email dùng cùng cách chuẩn hóa.
    public static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    // Chuỗi trống thành null. DB không lưu tên hoặc số điện thoại rỗng.
    public static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
