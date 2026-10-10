using Microsoft.Extensions.Logging.Abstractions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-03: contract test với sandbox GHN, chỉ đọc và chỉ chạy khi `GHN_LIVE_TESTS=1`. Tạo rồi hủy vận đơn trên sandbox thuộc SHIP-09.
public sealed class GhnSandboxContractTests
{
    private static GhnHttpClient CreateSandboxClient()
    {
        // Bật test live mà thiếu token là lỗi cấu hình của người chạy, nên thất bại rõ ràng thay vì bỏ qua âm thầm.
        var token = PickoryWebApplicationFactory.ResolveEnvironmentValue("GHN_TOKEN");
        var shopId = PickoryWebApplicationFactory.ResolveEnvironmentValue("GHN_SHOP_ID");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(shopId))
        {
            Assert.Fail("GHN_LIVE_TESTS=1 cần GHN_TOKEN và GHN_SHOP_ID của sandbox.");
        }

        var apiBase = PickoryWebApplicationFactory.ResolveEnvironmentValue("GHN_API_BASE") ?? "https://dev-online-gateway.ghn.vn";
        var httpClient = new HttpClient { BaseAddress = new Uri(apiBase), Timeout = Timeout.InfiniteTimeSpan };
        return new GhnHttpClient(httpClient, token, shopId, NullLogger<GhnHttpClient>.Instance);
    }

    [LiveGhnFact]
    public async Task Sandbox_GetProvinces_ReturnsProvincesWithNames()
    {
        var client = CreateSandboxClient();

        var provinces = await client.GetProvincesAsync(CancellationToken.None);

        Assert.NotEmpty(provinces);
        Assert.All(provinces, province =>
        {
            Assert.False(string.IsNullOrWhiteSpace(province.Name));
            Assert.False(string.IsNullOrWhiteSpace(province.Code));
        });
    }

    [LiveGhnFact]
    public async Task Sandbox_GetWards_ReturnsWardsOfFirstActiveProvince()
    {
        var client = CreateSandboxClient();
        var provinces = await client.GetProvincesAsync(CancellationToken.None);
        var province = provinces.First(candidate => candidate.Status == 1);

        var wards = await client.GetWardsAsync(province.Code, 0, 10, CancellationToken.None);

        Assert.NotEmpty(wards);
        Assert.All(wards, ward =>
        {
            Assert.False(string.IsNullOrWhiteSpace(ward.Name));
            Assert.Equal(province.Id, ward.ParentId);
        });
    }
}
