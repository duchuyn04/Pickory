using Pickory.Modules.Catalog;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog;

// Port từ `permissions.test.ts` của bản Node.
public sealed class CatalogPermissionsTests
{
    [Fact]
    // Quyền của catalog phải khớp nhóm `catalog` trong danh mục quyền của identity. Hai danh sách lệch nhau làm bảng phân quyền sai.
    public void All_MatchesIdentityCatalogGroup()
    {
        var identityCatalogPermissions = PermissionCatalog.Entries
            .Where(entry => entry.Group == "catalog")
            .Select(entry => entry.Id);

        Assert.Equal(identityCatalogPermissions, CatalogPermissions.All);
    }
}
