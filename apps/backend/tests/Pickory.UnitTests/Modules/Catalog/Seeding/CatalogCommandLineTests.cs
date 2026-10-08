using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Catalog.Seeding;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Seeding;

// CAT-12: nhận diện lệnh và từ chối seed ở môi trường Production.
public sealed class CatalogCommandLineTests
{
    [Fact]
    public void IsSeedCommand_OnlyMatchesTheSeedCommandAsFirstArgument()
    {
        Assert.True(CatalogCommandLine.IsSeedCommand(["catalog:seed"]));
        Assert.False(CatalogCommandLine.IsSeedCommand(["catalog:benchmark"]));
        Assert.False(CatalogCommandLine.IsSeedCommand(["identity:seed"]));
        Assert.False(CatalogCommandLine.IsSeedCommand([]));
    }

    [Fact]
    public void IsBenchmarkCommand_OnlyMatchesTheBenchmarkCommandAsFirstArgument()
    {
        Assert.True(CatalogCommandLine.IsBenchmarkCommand(["catalog:benchmark"]));
        Assert.False(CatalogCommandLine.IsBenchmarkCommand(["catalog:seed"]));
        Assert.False(CatalogCommandLine.IsBenchmarkCommand([]));
    }

    [Fact]
    public async Task RunSeedAsync_InProduction_RefusesWithoutTouchingTheDatabase()
    {
        // Provider rỗng: nếu lệnh đụng tới DB thì sẽ ném lỗi thiếu dịch vụ, nên test này chứng minh lệnh dừng trước đó.
        await using var services = new ServiceCollection().BuildServiceProvider();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CatalogCommandLine.RunSeedAsync(services, isProduction: true, output, error, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Production", error.ToString());
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunSeedAsync_WhenTheSeedFails_ReturnsOneAndPrintsTheReason()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var error = new StringWriter();

        var exitCode = await CatalogCommandLine.RunSeedAsync(
            services, isProduction: false, new StringWriter(), error, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Seed catalog thất bại", error.ToString());
    }
}
