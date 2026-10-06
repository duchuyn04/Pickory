namespace Pickory.Modules.Identity.Features.AdminRoles;

// `UserCount` cho giao diện biết role nào xóa được. `Permissions` được sắp xếp để thứ tự ổn định giữa các lần gọi.
public sealed record RoleResponse(string Id, string Code, string Name, bool IsSystem, IReadOnlyList<string> Permissions, int UserCount);
