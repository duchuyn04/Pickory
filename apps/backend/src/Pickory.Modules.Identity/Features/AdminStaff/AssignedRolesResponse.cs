namespace Pickory.Modules.Identity.Features.AdminStaff;

// Bộ role mới của nhân viên sau khi gán, giống response của bản Node.
public sealed record AssignedRolesResponse(string UserId, IReadOnlyList<string> Roles);
