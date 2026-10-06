using Pickory.Modules.Identity.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Actors;

// Kiểm tra cách tính quyền hiệu lực từ các role mà một người giữ.
public class ActorPermissionsTests
{
    [Fact]
    // Role `admin` nhận cả danh mục quyền theo thứ tự danh mục, kể cả khi DB không lưu quyền nào.
    public void Merge_AdminReceivesWholeCatalogInCatalogOrder()
    {
        // Người giữ thêm role khác vẫn nhận đủ quyền của admin.
        var permissions = ActorPermissions.Merge(["sales", "admin"], []);

        Assert.Equal(Permissions.All, permissions);
        // Số 28 khóa kích thước danh mục. Quyền bị thêm hoặc bớt ngoài ý muốn làm test này thất bại.
        Assert.Equal(28, permissions.Count);
    }

    [Fact]
    // Quyền gộp từ nhiều role giữ thứ tự gặp đầu tiên, bỏ quyền trùng và bỏ quyền lạ.
    public void Merge_UnionsRolesKeepsFirstSeenOrderAndDropsUnknownPermissions()
    {
        // Danh sách có một quyền trùng và một quyền đã bị xóa khỏi code nhưng còn trong DB.
        string[] storedPermissions =
        [
            Permissions.OrdersRead,
            "legacy:removed",
            Permissions.CustomersRead,
            Permissions.OrdersRead,
            Permissions.ReportsRead,
        ];

        var permissions = ActorPermissions.Merge(["sales", "support"], storedPermissions);

        // Thứ tự giống `Set` của bản Node. Quyền lạ không có hiệu lực.
        Assert.Equal([Permissions.OrdersRead, Permissions.CustomersRead, Permissions.ReportsRead], permissions);
    }

    [Fact]
    // Role không có quyền nào cho danh sách rỗng, không cho quyền mặc định.
    public void Merge_RoleWithoutPermissionsGivesEmptyList()
    {
        Assert.Empty(ActorPermissions.Merge(["customer"], []));
    }
}
