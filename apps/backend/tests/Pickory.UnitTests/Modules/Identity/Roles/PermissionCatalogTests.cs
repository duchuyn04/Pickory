using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Roles;

// Danh mục quyền là dữ liệu cho bảng tick trên trang quản lý role.
public class PermissionCatalogTests
{
    [Fact]
    // Mỗi quyền trong code có đúng một mục hiển thị, theo cùng thứ tự. Giao diện không thiếu quyền nào để tick.
    public void Entries_CoverEveryPermissionInCatalogOrder()
    {
        Assert.Equal(Permissions.All, PermissionCatalog.Entries.Select(entry => entry.Id));
    }

    [Fact]
    // Nhóm, tên và mô tả giữ nguyên bản Node. Frontend gom quyền theo `group` và hiển thị `name`.
    public void Entries_KeepNodeMetadata()
    {
        var staffRead = PermissionCatalog.Entries.Single(entry => entry.Id == Permissions.StaffRead);
        var reportsExport = PermissionCatalog.Entries.Single(entry => entry.Id == Permissions.ReportsExport);

        Assert.Equal(new PermissionCatalogEntry(Permissions.StaffRead, "identity", "Xem nhân viên", "Xem danh sách tài khoản nhân viên"), staffRead);
        Assert.Equal(
            new PermissionCatalogEntry(Permissions.ReportsExport, "admin-console", "Xuất dữ liệu báo cáo", "Xuất báo cáo, đơn hàng ra file Excel/CSV"),
            reportsExport);
    }
}
