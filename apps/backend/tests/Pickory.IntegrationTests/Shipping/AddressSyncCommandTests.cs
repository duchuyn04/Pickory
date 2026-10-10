using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Addresses;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-04 lát 6: lệnh `shipping:sync-addresses` chạy trên API test với client GHN giả, và factory không chạy worker nền.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AddressSyncCommandTests(PickoryWebApplicationFactory factory)
    : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    public async Task InitializeAsync() => await ClearAsync();

    public async Task DisposeAsync()
    {
        // Trả client GHN giả về trạng thái không lỗi, để class test khác dùng chung factory không bị ảnh hưởng.
        factory.GhnClient.ClearFailures();
        await ClearAsync();
    }

    private async Task ClearAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
        DotnetSchemaIntegrationTests.AssertTestConnection(context);
        await AddressTestDatabase.ClearAsync(context);
    }

    [Fact]
    public async Task Command_WithFakeGhn_Returns0_PrintsCounts_AndWritesCatalog()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ShippingCommandLine.RunSyncAddressesAsync(factory.Services, output, error, CancellationToken.None);

        Assert.Equal(0, exitCode);
        // Client giả có 3 tỉnh/thành và 6 phường/xã, gồm cả dòng đã tắt.
        Assert.Contains("3 tỉnh/thành, 6 phường/xã", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        Assert.Equal(3, await context.AddressProvinces.CountAsync());
        Assert.Equal(6, await context.AddressWards.CountAsync());
    }

    [Fact]
    public async Task Command_WhenGhnUnavailable_Returns1_PrintsReasonToError_AndWritesNothing()
    {
        factory.GhnClient.SetFailure(FakeGhnOperation.GetProvinces, FakeGhnFailure.Unavailable);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ShippingCommandLine.RunSyncAddressesAsync(factory.Services, output, error, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("thất bại", error.ToString());
        Assert.Equal(string.Empty, output.ToString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        Assert.Equal(0, await context.AddressProvinces.CountAsync());
    }

    [Fact]
    // `e2e:start` gọi hàm công khai này sau migration, để Playwright luôn có danh mục địa chỉ khi mở màn.
    public async Task PublicSync_WithFakeGhn_WritesCatalog()
    {
        await ShippingModule.SyncAddressesAsync(factory.Services, CancellationToken.None);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        Assert.Equal(3, await context.AddressProvinces.CountAsync());
        Assert.Equal(6, await context.AddressWards.CountAsync());
    }

    [Fact]
    // Đồng bộ lỗi thì `e2e:start` phải dừng với lý do rõ ràng, không mở API với danh mục rỗng.
    public async Task PublicSync_WhenGhnUnavailable_ThrowsWithReasonAndWritesNothing()
    {
        factory.GhnClient.SetFailure(FakeGhnOperation.GetProvinces, FakeGhnFailure.Unavailable);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ShippingModule.SyncAddressesAsync(factory.Services, CancellationToken.None));

        Assert.Contains("danh mục địa chỉ", exception.Message);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        Assert.Equal(0, await context.AddressProvinces.CountAsync());
    }

    [Fact]
    public void Factory_DoesNotRunAddressSyncWorker()
    {
        var hostedServices = factory.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, service => service is AddressSyncWorker);
    }
}
