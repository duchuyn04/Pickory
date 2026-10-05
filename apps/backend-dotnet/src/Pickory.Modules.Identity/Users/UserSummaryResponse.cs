namespace Pickory.Modules.Identity.Users;

// Hồ sơ rút gọn sau đăng nhập nhân viên và đăng nhập Google. Bản Node không trả số điện thoại ở hai response này.
public sealed record UserSummaryResponse(string Id, string Email, string? Name, string Kind)
{
    public const string StaffKind = "staff";
    public const string CustomerKind = "customer";
}
