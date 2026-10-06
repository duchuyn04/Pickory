namespace Pickory.Modules.Identity.Features.AdminRoles;

// Trường không gửi thì giữ nguyên. Thiếu `Permissions` nghĩa là chỉ đổi tên, không xóa hết quyền.
public sealed record UpdateRoleRequest(string? Name, string[]? Permissions);
