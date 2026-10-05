using Microsoft.AspNetCore.Builder;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Contracts.Authorization;

// Kiểm tra ba quy tắc chặn request theo actor. Mã lỗi và thông điệp giữ nguyên bản Node.
public class ActorAuthorizationTests
{
    [Fact]
    // Khách vãng lai nhận 401 vì API chưa biết người gửi là ai.
    public void EnsureAuthenticated_GuestReceives401()
    {
        var exception = Assert.Throws<AppException>(() => ActorAuthorization.EnsureAuthenticated(CreateGuest()));

        // Thông điệp giống bản Node vì frontend hiển thị trực tiếp.
        Assert.Equal("UNAUTHORIZED", exception.Code);
        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("Vui lòng đăng nhập để tiếp tục.", exception.Message);
    }

    [Fact]
    // Khách vãng lai vào khu vực nhân viên nhận 401, không nhận 403. Kiểm tra đăng nhập chạy trước.
    public void EnsureStaff_GuestReceives401BeforeStaffCheck()
    {
        var exception = Assert.Throws<AppException>(() => ActorAuthorization.EnsureStaff(CreateGuest()));

        // Mã 401 cho frontend biết cần mở trang đăng nhập.
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    // Khách hàng đã đăng nhập nhận 403 ở khu vực nhân viên. API đã biết người gửi nhưng người này không có quyền vào.
    public void EnsureStaff_CustomerReceives403()
    {
        var customer = CreateSignedIn(ActorKind.Customer, []);

        var exception = Assert.Throws<AppException>(() => ActorAuthorization.EnsureStaff(customer));

        // Mã và thông điệp khu vực giống bản Node.
        Assert.Equal("FORBIDDEN", exception.Code);
        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("Bạn không có quyền truy cập khu vực này.", exception.Message);
    }

    [Fact]
    // Nhân viên vào khu vực nhân viên mà không cần quyền cụ thể nào.
    public void EnsureStaff_StaffPasses()
    {
        // Hàm không ném lỗi là kết quả mong đợi.
        ActorAuthorization.EnsureStaff(CreateSignedIn(ActorKind.Staff, []));
    }

    [Fact]
    // Thiếu một trong nhiều quyền thì bị từ chối. Danh sách nhiều quyền nghĩa là cần đủ mọi quyền.
    public void EnsurePermissions_MissingOneOfSeveralPermissionsReceives403()
    {
        // Nhân viên chỉ có quyền đọc đơn, không có quyền hủy đơn.
        var staff = CreateSignedIn(ActorKind.Staff, [Permissions.OrdersRead]);

        var exception = Assert.Throws<AppException>(() =>
            ActorAuthorization.EnsurePermissions(staff, [Permissions.OrdersRead, Permissions.OrdersCancel]));

        // Thông điệp thiếu quyền khác thông điệp khu vực. Frontend phân biệt được hai trường hợp.
        Assert.Equal("FORBIDDEN", exception.Code);
        Assert.Equal("Bạn không có đủ quyền thực hiện thao tác này.", exception.Message);
    }

    [Fact]
    // Nhân viên có đủ mọi quyền được cho qua.
    public void EnsurePermissions_AllPermissionsPresentPasses()
    {
        var staff = CreateSignedIn(ActorKind.Staff, [Permissions.OrdersRead, Permissions.OrdersCancel]);

        // Hàm không ném lỗi là kết quả mong đợi.
        ActorAuthorization.EnsurePermissions(staff, [Permissions.OrdersRead, Permissions.OrdersCancel]);
    }

    [Fact]
    // Khách vãng lai nhận 401 ở route cần quyền. Kiểm tra đăng nhập chạy trước kiểm tra quyền.
    public void EnsurePermissions_GuestReceives401()
    {
        var exception = Assert.Throws<AppException>(() =>
            ActorAuthorization.EnsurePermissions(CreateGuest(), [Permissions.OrdersRead]));

        // Mã 401, không phải 403, vì API chưa biết người gửi.
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    // Tên quyền gõ sai làm lỗi ngay lúc khai báo route. Lỗi lộ ra khi khởi động, không chờ người dùng gọi route.
    public void RequirePermission_UnknownPermissionFailsWhenRouteIsDeclared()
    {
        var builder = new RecordingEndpointConventionBuilder();

        // `orders:typo` không có trong danh mục quyền.
        Assert.Throws<ArgumentException>(() => builder.RequirePermission("orders:typo"));
    }

    // Tạo khách vãng lai không có role, quyền hay consent. Các actor khác dựa trên giá trị gốc này.
    private static Actor CreateGuest()
    {
        return new Actor
        {
            AnonymousId = "0b4f8a8e-3f7e-4d6a-9a52-6d2f1c0c9e11",
            Kind = ActorKind.Guest,
            Roles = [],
            Permissions = [],
            Consent = ConsentState.AllDisabled,
        };
    }

    // Tạo actor đã đăng nhập. Test chỉ đổi loại người dùng và danh sách quyền.
    private static Actor CreateSignedIn(ActorKind kind, IReadOnlyList<string> permissions)
    {
        return CreateGuest() with
        {
            UserId = "user-1",
            Kind = kind,
            Permissions = permissions,
        };
    }

    // Builder giả chỉ ghi nhận quy ước. Test không cần dựng route thật để kiểm tra bước khai báo.
    private sealed class RecordingEndpointConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention)
        {
            Conventions.Add(convention);
        }
    }
}
