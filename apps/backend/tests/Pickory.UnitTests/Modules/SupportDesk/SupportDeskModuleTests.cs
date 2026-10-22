using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.SupportDesk;
using Pickory.Modules.SupportDesk.Persistence;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk;

// SD-02: `AddSupportDeskModule` là điểm đăng ký duy nhất của module, nên `Program.cs` chỉ cần gọi hàm này.
public sealed class SupportDeskModuleTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    [Fact]
    public void AddSupportDeskModule_RegistersDbContext()
    {
        var services = new ServiceCollection();

        services.AddSupportDeskModule(ConnectionString);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(SupportDeskDbContext));
    }
}
