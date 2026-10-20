using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.ActivityTracking;
using Pickory.Modules.ActivityTracking.Persistence;
using Xunit;

namespace Pickory.UnitTests.Modules.ActivityTracking;

// ACT-04: `AddActivityTrackingModule` là điểm đăng ký duy nhất của module, nên `Program.cs` chỉ cần gọi hàm này.
public sealed class ActivityTrackingModuleTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    [Fact]
    public void AddActivityTrackingModule_RegistersDbContext()
    {
        var services = new ServiceCollection();

        services.AddActivityTrackingModule(ConnectionString);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ActivityTrackingDbContext));
    }
}
