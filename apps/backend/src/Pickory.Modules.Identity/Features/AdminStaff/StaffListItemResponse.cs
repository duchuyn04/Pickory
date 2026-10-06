namespace Pickory.Modules.Identity.Features.AdminStaff;

// Một dòng trên trang quản lý nhân viên. `LockedAt` có giá trị nghĩa là nhân viên đang bị khóa.
public sealed record StaffListItemResponse(
    string Id,
    string Email,
    string? Name,
    string? Phone,
    bool EmailVerified,
    string Kind,
    DateTime? LockedAt,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles);
