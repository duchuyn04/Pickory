using Pickory.Modules.Identity.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Actors;

public class ActorPermissionsTests
{
    [Fact]
    public void Merge_AdminReceivesWholeCatalogInCatalogOrder()
    {
        var permissions = ActorPermissions.Merge(["sales", "admin"], []);

        Assert.Equal(Permissions.All, permissions);
        Assert.Equal(28, permissions.Count);
    }

    [Fact]
    public void Merge_UnionsRolesKeepsFirstSeenOrderAndDropsUnknownPermissions()
    {
        string[] storedPermissions =
        [
            Permissions.OrdersRead,
            "legacy:removed",
            Permissions.CustomersRead,
            Permissions.OrdersRead,
            Permissions.ReportsRead,
        ];

        var permissions = ActorPermissions.Merge(["sales", "support"], storedPermissions);

        Assert.Equal([Permissions.OrdersRead, Permissions.CustomersRead, Permissions.ReportsRead], permissions);
    }

    [Fact]
    public void Merge_RoleWithoutPermissionsGivesEmptyList()
    {
        Assert.Empty(ActorPermissions.Merge(["customer"], []));
    }
}
