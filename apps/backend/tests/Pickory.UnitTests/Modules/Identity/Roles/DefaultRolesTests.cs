using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Roles;

// Role mặc định được seed vào DB mới. Role này không được mang quyền đã bị xóa khỏi code.
public class DefaultRolesTests
{
    [Fact]
    // Mọi quyền của role mặc định phải nằm trong danh mục quyền hiện tại. Quyền AI đã gỡ không được còn sót.
    public void Permissions_OfEveryDefaultRoleAreKnown()
    {
        foreach (var role in DefaultRoles.All)
        {
            Assert.All(role.Permissions, permission => Assert.True(
                Permissions.IsKnown(permission), $"Role {role.Code} có quyền không còn trong danh mục: {permission}"));
        }
    }

    [Fact]
    // Role `sales` từng có quyền xem sử dụng AI. Sau khi gỡ module AI, role này chỉ giữ các quyền còn lại.
    public void SalesRole_HasNoAiPermissions()
    {
        var sales = DefaultRoles.All.Single(role => role.Code == DefaultRoles.SalesCode);

        Assert.DoesNotContain("ai-usage:read", sales.Permissions);
        Assert.DoesNotContain("ai-settings:manage", sales.Permissions);
    }
}
