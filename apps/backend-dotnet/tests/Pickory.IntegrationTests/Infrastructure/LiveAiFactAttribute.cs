using Xunit;

namespace Pickory.IntegrationTests.Infrastructure;

// Test gọi API AI thật chỉ chạy khi `AI_LIVE_TESTS=1`. Bộ test mặc định không tốn tiền và không cần mạng.
public sealed class LiveAiFactAttribute : FactAttribute
{
    public LiveAiFactAttribute()
    {
        if (PickoryWebApplicationFactory.ResolveEnvironmentValue("AI_LIVE_TESTS") != "1")
        {
            Skip = "Chỉ chạy khi AI_LIVE_TESTS=1.";
        }
    }
}
