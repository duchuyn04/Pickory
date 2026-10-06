namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Hồ sơ khách cho nhân viên tra cứu. Record liệt kê từng trường, nên hash mật khẩu không bao giờ lọt ra API.
public sealed record CustomerResponse(
    string Id,
    string Email,
    string? Name,
    string? Phone,
    bool EmailVerified,
    string Kind,
    DateTime? LockedAt,
    DateTime CreatedAt);
