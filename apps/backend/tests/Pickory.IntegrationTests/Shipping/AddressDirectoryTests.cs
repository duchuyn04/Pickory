using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// COM-07: `IAddressDirectory` tra tên chuẩn từ danh mục. Bốn trường hợp không hợp lệ cùng trả `null`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AddressDirectoryTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string ActiveProvince = "7000001";
    private const string OtherProvince = "7000002";
    private const string DisabledProvince = "7000003";
    private const string ActiveWard = "7100001";
    private const string DisabledWard = "7100002";
    private const string WardOfDisabledProvince = "7100003";

    private static readonly DateTime SyncedAt = new(2026, 10, 10, 6, 30, 15, DateTimeKind.Utc);

    private readonly PickoryWebApplicationFactory _factory;

    public AddressDirectoryTests(PickoryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await WithContextAsync(AddressTestDatabase.ClearAsync);
        await WithContextAsync(async context =>
        {
            context.AddressProvinces.AddRange(
                AddressTestDatabase.Province(ActiveProvince, "Tỉnh Một", isActive: true, SyncedAt),
                AddressTestDatabase.Province(OtherProvince, "Tỉnh Hai", isActive: true, SyncedAt),
                AddressTestDatabase.Province(DisabledProvince, "Tỉnh đã tắt", isActive: false, SyncedAt));
            await context.SaveChangesAsync();
            context.AddressWards.AddRange(
                AddressTestDatabase.Ward(ActiveWard, ActiveProvince, "Phường Một", isActive: true, SyncedAt),
                AddressTestDatabase.Ward(DisabledWard, ActiveProvince, "Phường đã tắt", isActive: false, SyncedAt),
                AddressTestDatabase.Ward(WardOfDisabledProvince, DisabledProvince, "Phường của tỉnh tắt", isActive: true, SyncedAt));
            await context.SaveChangesAsync();
        });
    }

    public Task DisposeAsync() => WithContextAsync(AddressTestDatabase.ClearAsync);

    private async Task WithContextAsync(Func<ShippingDbContext, Task> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await action(context);
    }

    private async Task<AddressNames?> FindAsync(string provinceCode, string wardCode)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IAddressDirectory>();
        return await directory.FindAsync(provinceCode, wardCode, CancellationToken.None);
    }

    [Fact]
    public async Task Find_ActiveProvinceAndWard_ReturnsCatalogNames()
    {
        var names = await FindAsync(ActiveProvince, ActiveWard);

        Assert.Equal(new AddressNames("Tỉnh Một", "Phường Một"), names);
    }

    [Fact]
    public async Task Find_UnknownProvince_ReturnsNull()
    {
        Assert.Null(await FindAsync("9999999", ActiveWard));
    }

    [Fact]
    public async Task Find_UnknownWard_ReturnsNull()
    {
        Assert.Null(await FindAsync(ActiveProvince, "9999999"));
    }

    [Fact]
    public async Task Find_WardOfAnotherProvince_ReturnsNull()
    {
        Assert.Null(await FindAsync(OtherProvince, ActiveWard));
    }

    [Fact]
    public async Task Find_DisabledWard_ReturnsNull()
    {
        Assert.Null(await FindAsync(ActiveProvince, DisabledWard));
    }

    [Fact]
    public async Task Find_ActiveWardOfDisabledProvince_ReturnsNull()
    {
        Assert.Null(await FindAsync(DisabledProvince, WardOfDisabledProvince));
    }
}
