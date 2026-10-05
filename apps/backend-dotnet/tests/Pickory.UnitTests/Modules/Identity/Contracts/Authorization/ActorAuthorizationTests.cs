using Microsoft.AspNetCore.Builder;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Contracts.Authorization;

public class ActorAuthorizationTests
{
    [Fact]
    public void EnsureAuthenticated_GuestReceives401()
    {
        var exception = Assert.Throws<AppException>(() => ActorAuthorization.EnsureAuthenticated(CreateGuest()));

        Assert.Equal("UNAUTHORIZED", exception.Code);
        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("Vui lòng đăng nhập để tiếp tục.", exception.Message);
    }

    [Fact]
    public void EnsureStaff_GuestReceives401BeforeStaffCheck()
    {
        var exception = Assert.Throws<AppException>(() => ActorAuthorization.EnsureStaff(CreateGuest()));

        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public void EnsureStaff_CustomerReceives403()
    {
        var customer = CreateSignedIn(ActorKind.Customer, []);

        var exception = Assert.Throws<AppException>(() => ActorAuthorization.EnsureStaff(customer));

        Assert.Equal("FORBIDDEN", exception.Code);
        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("Bạn không có quyền truy cập khu vực này.", exception.Message);
    }

    [Fact]
    public void EnsureStaff_StaffPasses()
    {
        ActorAuthorization.EnsureStaff(CreateSignedIn(ActorKind.Staff, []));
    }

    [Fact]
    public void EnsurePermissions_MissingOneOfSeveralPermissionsReceives403()
    {
        var staff = CreateSignedIn(ActorKind.Staff, [Permissions.OrdersRead]);

        var exception = Assert.Throws<AppException>(() =>
            ActorAuthorization.EnsurePermissions(staff, [Permissions.OrdersRead, Permissions.OrdersCancel]));

        Assert.Equal("FORBIDDEN", exception.Code);
        Assert.Equal("Bạn không có đủ quyền thực hiện thao tác này.", exception.Message);
    }

    [Fact]
    public void EnsurePermissions_AllPermissionsPresentPasses()
    {
        var staff = CreateSignedIn(ActorKind.Staff, [Permissions.OrdersRead, Permissions.OrdersCancel]);

        ActorAuthorization.EnsurePermissions(staff, [Permissions.OrdersRead, Permissions.OrdersCancel]);
    }

    [Fact]
    public void EnsurePermissions_GuestReceives401()
    {
        var exception = Assert.Throws<AppException>(() =>
            ActorAuthorization.EnsurePermissions(CreateGuest(), [Permissions.OrdersRead]));

        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public void RequirePermission_UnknownPermissionFailsWhenRouteIsDeclared()
    {
        var builder = new RecordingEndpointConventionBuilder();

        Assert.Throws<ArgumentException>(() => builder.RequirePermission("orders:typo"));
    }

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
