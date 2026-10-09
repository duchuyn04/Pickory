namespace Pickory.Modules.Identity.Features.AdminStaff;

// Mảng rỗng hợp lệ và nghĩa là gỡ hết role. Phiên của nhân viên khi đó bị xóa.
public sealed record AssignRolesRequest(string[]? RoleCodes);
