namespace Pickory.Modules.Identity.Features.AdminStaff;

// `RoleCodes` có ít nhất một role. Nhân viên không có role nào thì không làm được việc gì.
public sealed record InviteStaffRequest(string? Email, string? Name, string[]? RoleCodes);
