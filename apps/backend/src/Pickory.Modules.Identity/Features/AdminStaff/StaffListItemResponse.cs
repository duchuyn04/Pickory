namespace Pickory.Modules.Identity.Features.AdminStaff;

// Một dòng trên trang quản lý nhân viên. `Status` là `active`, `invited` hoặc `locked`, theo cùng luật với bộ lọc `status`.
public sealed record StaffListItemResponse(
    string Id,
    string Email,
    string? Name,
    string? Phone,
    bool EmailVerified,
    string Kind,
    DateTime? LockedAt,
    string Status,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles);
