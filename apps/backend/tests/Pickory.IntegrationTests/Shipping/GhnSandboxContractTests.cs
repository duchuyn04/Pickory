using Microsoft.Extensions.Logging.Abstractions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Ghn;
using Xunit;
using Xunit.Abstractions;

namespace Pickory.IntegrationTests.Shipping;

// Contract test với sandbox GHN, chỉ chạy khi `GHN_LIVE_TESTS=1`. SHIP-03 thêm các test đọc, SHIP-09 thêm test tạo rồi hủy đơn và đo p95 báo phí.
public sealed class GhnSandboxContractTests
{
    private const int FeeSampleCount = 20;
    private static readonly TimeSpan FeeP95Budget = TimeSpan.FromSeconds(2);

    private readonly ITestOutputHelper _output;

    public GhnSandboxContractTests(ITestOutputHelper output)
    {
        _output = output;
    }

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

    // Lấy một phường đang dùng từ danh mục thật. Nhờ vậy test không phụ thuộc mã cố định mà GHN có thể đổi.
    private static async Task<(GhnProvince Province, GhnWard Ward)> PickActiveWardAsync(GhnHttpClient client)
    {
        var provinces = await client.GetProvincesAsync(CancellationToken.None);
        var province = provinces.First(candidate => candidate.Status == 1);
        var wards = await client.GetWardsAsync(province.Code, 0, 10, CancellationToken.None);
        return (province, wards.First(candidate => candidate.Status == 1));
    }

    [LiveGhnFact]
    // Địa chỉ nhận 2 cấp phải được GHN chấp nhận khi báo phí (ADR 0003, tiêu chí 1). Test chỉ đọc, không tạo đơn.
    public async Task Sandbox_CalculateFee_TwoLevelAddress_ReturnsPositiveFee()
    {
        var client = CreateSandboxClient();
        var (province, ward) = await PickActiveWardAsync(client);
        Assert.True(GhnAddressIds.TryParse(province.Code, ward.Code, out var provinceId, out var wardId));

        var fee = await client.CalculateFeeAsync(
            new GhnFeeRequest(1000, 20, 15, 10, ServiceTypeId: 2, InsuranceValue: 0, CodValue: 0, provinceId, wardId),
            CancellationToken.None);

        Assert.True(fee.Total > 0);
        Assert.True(fee.ServiceFee > 0);
    }

    [LiveGhnFact]
    // API ngày giao nhận cùng 3 trường địa chỉ như báo phí (ADR 0003, tiêu chí 4).
    public async Task Sandbox_GetLeadTime_TwoLevelAddress_ReturnsFutureUnixSeconds()
    {
        var client = CreateSandboxClient();
        var (province, ward) = await PickActiveWardAsync(client);
        Assert.True(GhnAddressIds.TryParse(province.Code, ward.Code, out var provinceId, out var wardId));

        var leadTime = await client.GetLeadTimeAsync(
            new GhnLeadTimeRequest(provinceId, wardId, ServiceTypeId: 2, Weight: 1000, Length: 20, Width: 15, Height: 10),
            CancellationToken.None);

        // Thời điểm giao dự kiến phải sau hiện tại. Giá trị 0 hoặc quá khứ cho thấy GHN không đọc được địa chỉ.
        Assert.NotNull(leadTime.Leadtime);
        Assert.True(leadTime.Leadtime > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [LiveGhnFact]
    // Tiêu chí 3 của ADR 0003: tạo đơn 2 cấp bằng tên tỉnh và tên phường, rồi hủy và thấy trạng thái `cancel`.
    public async Task Sandbox_CreateThenCancelOrder_EndsWithCancelStatus()
    {
        var client = CreateSandboxClient();
        var (province, ward) = await PickActiveWardAsync(client);
        // Mã duy nhất mỗi lần chạy. GHN trả lại đơn cũ khi `client_order_code` trùng nên mã cố định làm test không tạo đơn mới.
        var clientOrderCode = $"ship09-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var createRequest = new GhnCreateOrderRequest(
            ToName: "Pickory Test",
            ToPhone: "0900000000",
            ToAddress: "1 Duong Thu",
            ToProvinceName: province.Name,
            ToWardName: ward.Name,
            ClientOrderCode: clientOrderCode,
            Weight: 500,
            Length: 15,
            Width: 10,
            Height: 5,
            ServiceTypeId: 2,
            PaymentTypeId: 2,
            CodAmount: 0,
            InsuranceValue: 0,
            Content: "Don thu SHIP-09")
        {
            Items = [new GhnOrderItem("Hang thu", 1, 500, 15, 10, 5)],
        };

        string? createdOrderCode = null;
        try
        {
            var created = await client.CreateOrderAsync(createRequest, CancellationToken.None);
            createdOrderCode = created.OrderCode;
            Assert.False(string.IsNullOrWhiteSpace(created.OrderCode));

            // Đơn vừa tạo phải tra lại được bằng mã GHN và mang đúng mã khách.
            var detailAfterCreate = await client.GetOrderDetailAsync(created.OrderCode, CancellationToken.None);
            Assert.Equal(clientOrderCode, detailAfterCreate.ClientOrderCode);

            await client.CancelOrderAsync(created.OrderCode, GhnCancelReason.Other, "Test hợp đồng SHIP-09", CancellationToken.None);

            var detailAfterCancel = await client.GetOrderDetailAsync(created.OrderCode, CancellationToken.None);
            Assert.Equal("cancel", detailAfterCancel.Status);
            createdOrderCode = null;
        }
        finally
        {
            // Hủy trong `finally` để test lỗi giữa chừng không để lại đơn sống trên sandbox. Lỗi hủy ở đây bị bỏ qua vì lỗi gốc quan trọng hơn.
            if (createdOrderCode is not null)
            {
                try
                {
                    await client.CancelOrderAsync(createdOrderCode, GhnCancelReason.Other, "Dọn đơn test SHIP-09", CancellationToken.None);
                }
                catch (Exception cleanupError)
                {
                    _output.WriteLine($"Không hủy được đơn {createdOrderCode}: {cleanupError.Message}");
                }
            }
        }
    }

    [LiveGhnFact]
    // Tiêu chí 4 của SHIP-09: p95 của báo phí tuần tự trên sandbox phải dưới 2 giây.
    public async Task Sandbox_CalculateFee_P95IsUnderBudget()
    {
        var client = CreateSandboxClient();
        var (province, ward) = await PickActiveWardAsync(client);
        Assert.True(GhnAddressIds.TryParse(province.Code, ward.Code, out var provinceId, out var wardId));
        var feeRequest = new GhnFeeRequest(1000, 20, 15, 10, ServiceTypeId: 2, InsuranceValue: 0, CodValue: 0, provinceId, wardId);

        // Gọi tuần tự để mỗi mẫu đo đúng một lượt báo phí, không bị nhiễu bởi các lượt chạy song song.
        var elapsedSamples = new List<TimeSpan>();
        for (var sampleIndex = 0; sampleIndex < FeeSampleCount; sampleIndex++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await client.CalculateFeeAsync(feeRequest, CancellationToken.None);
            stopwatch.Stop();
            elapsedSamples.Add(stopwatch.Elapsed);
        }

        // Dùng hạng gần nhất (nearest rank): với 20 mẫu, p95 là mẫu thứ 19 sau khi sắp xếp.
        var sortedSamples = elapsedSamples.OrderBy(sample => sample).ToList();
        var p95Rank = (int)Math.Ceiling(0.95 * sortedSamples.Count);
        var p95 = sortedSamples[p95Rank - 1];
        _output.WriteLine($"Báo phí sandbox: {sortedSamples.Count} mẫu, min {sortedSamples[0].TotalMilliseconds:F0} ms, p95 {p95.TotalMilliseconds:F0} ms, max {sortedSamples[^1].TotalMilliseconds:F0} ms.");

        Assert.True(p95 < FeeP95Budget, $"p95 báo phí {p95.TotalMilliseconds:F0} ms, vượt ngưỡng {FeeP95Budget.TotalMilliseconds:F0} ms.");
    }
}
