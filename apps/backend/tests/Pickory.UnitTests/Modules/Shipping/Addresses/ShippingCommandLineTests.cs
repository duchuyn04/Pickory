using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Addresses;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Addresses;

// SHIP-04 lát 6: lệnh `shipping:sync-addresses` trả mã thoát 0 khi thành công và 1 khi thất bại, kèm lý do ở stderr.
public sealed class ShippingCommandLineTests
{
    private static ServiceProvider BuildServices(StubAddressSyncProcessor processor)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAddressSyncProcessor>(_ => processor);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void IsShippingCommand_RecognizesOnlySyncAddressesCommand()
    {
        Assert.True(ShippingModule.IsShippingCommand(["shipping:sync-addresses"]));
        Assert.False(ShippingModule.IsShippingCommand([]));
        Assert.False(ShippingModule.IsShippingCommand(["catalog:seed"]));
        Assert.False(ShippingModule.IsShippingCommand(["shipping:other"]));
    }

    [Fact]
    public async Task Run_Success_Returns0_AndPrintsCounts()
    {
        var processor = new StubAddressSyncProcessor { Result = AddressSyncResult.Success(34, 3321) };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ShippingCommandLine.RunSyncAddressesAsync(BuildServices(processor), output, error, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("34 tỉnh/thành", output.ToString());
        Assert.Contains("3321 phường/xã", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public async Task Run_FailedResult_Returns1_AndPrintsReasonToError()
    {
        var processor = new StubAddressSyncProcessor { Result = AddressSyncResult.Failure("Đơn vị vận chuyển đang gián đoạn.") };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ShippingCommandLine.RunSyncAddressesAsync(BuildServices(processor), output, error, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Đơn vị vận chuyển đang gián đoạn.", error.ToString());
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task Run_ProcessorThrows_Returns1_InsteadOfCrashing()
    {
        var processor = new StubAddressSyncProcessor { OnSync = _ => throw new InvalidOperationException("Không kết nối được DB") };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ShippingCommandLine.RunSyncAddressesAsync(BuildServices(processor), output, error, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Không kết nối được DB", error.ToString());
    }
}
