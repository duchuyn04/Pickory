using Xunit;

namespace Pickory.IntegrationTests.Infrastructure;

// Test gọi sandbox GHN thật chỉ chạy khi `GHN_LIVE_TESTS=1`. Bộ test mặc định không cần mạng và không cần token.
public sealed class LiveGhnFactAttribute : FactAttribute
{
    public LiveGhnFactAttribute()
    {
        if (PickoryWebApplicationFactory.ResolveEnvironmentValue("GHN_LIVE_TESTS") != "1")
        {
            Skip = "Chỉ chạy khi GHN_LIVE_TESTS=1.";
        }
    }
}
