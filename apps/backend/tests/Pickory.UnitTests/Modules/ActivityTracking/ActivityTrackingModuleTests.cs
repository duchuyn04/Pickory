using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.ActivityTracking;
using Pickory.Modules.ActivityTracking.Counters;
using Pickory.Modules.ActivityTracking.Features.CollectEvents;
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

    [Fact]
    public void AddActivityTrackingModule_RegistersTimeProvider()
    {
        var services = new ServiceCollection();

        services.AddActivityTrackingModule(ConnectionString);

        // Module tự đăng ký đồng hồ, không dựa vào module khác đã đăng ký trước.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void AddActivityTrackingModule_RegistersDailyCounterWriterAsScoped()
    {
        var services = new ServiceCollection();

        services.AddActivityTrackingModule(ConnectionString);

        // Bộ ghi giữ `ActivityTrackingDbContext` của request, nên vòng đời phải là scoped.
        var descriptor = Assert.Single(services, row => row.ServiceType == typeof(DailyCounterWriter));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddActivityTrackingModule_RegistersProductViewRateLimiterAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddActivityTrackingModule(ConnectionString);

        // Bộ đếm tần suất nằm trong bộ nhớ, nên mọi request phải dùng chung một instance.
        var descriptor = Assert.Single(services, row => row.ServiceType == typeof(ProductViewRateLimiter));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
