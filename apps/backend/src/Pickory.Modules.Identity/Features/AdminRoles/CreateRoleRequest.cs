namespace Pickory.Modules.Identity.Features.AdminRoles;

// `Permissions` có thể thiếu. Role mới khi đó chưa có quyền nào, giống giá trị mặc định của bản Node.
public sealed record CreateRoleRequest(string? Code, string? Name, string[]? Permissions);
