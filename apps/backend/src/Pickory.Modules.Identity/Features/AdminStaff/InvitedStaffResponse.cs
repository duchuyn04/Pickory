namespace Pickory.Modules.Identity.Features.AdminStaff;

// Nhân viên vừa được mời. Response không chứa token của link mời.
public sealed record InvitedStaffResponse(string Id, string Email, string? Name, IReadOnlyList<string> Roles);
