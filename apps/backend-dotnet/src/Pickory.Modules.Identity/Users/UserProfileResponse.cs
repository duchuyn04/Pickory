namespace Pickory.Modules.Identity.Users;

// Hồ sơ đầy đủ sau đăng ký, đăng nhập khách và sửa hồ sơ. Response không bao giờ chứa hash mật khẩu.
public sealed record UserProfileResponse(string Id, string Email, string? Name, string? Phone, string Kind)
{
    public const string CustomerKind = "customer";
}
