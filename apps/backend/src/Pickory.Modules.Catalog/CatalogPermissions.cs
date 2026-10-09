using Pickory.Modules.Identity.Contracts.Authorization;

namespace Pickory.Modules.Catalog;

// Quyền back-office của catalog. Danh sách giữ cùng thứ tự với nhóm `catalog` trong danh mục quyền của identity.
public static class CatalogPermissions
{
    public static IReadOnlyList<string> All { get; } =
    [
        Permissions.ProductsRead,
        Permissions.ProductsWrite,
        Permissions.InventoryWrite,
    ];
}
