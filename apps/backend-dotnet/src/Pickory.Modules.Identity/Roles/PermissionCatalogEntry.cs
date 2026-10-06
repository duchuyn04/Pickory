namespace Pickory.Modules.Identity.Roles;

// `Id` dùng trong code. `Name` và `Description` là chữ tiếng Việt hiển thị cho người dùng.
public sealed record PermissionCatalogEntry(string Id, string Group, string Name, string Description);
