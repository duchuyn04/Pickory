using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
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

    // Địa chỉ lấy hàng giống cấu hình dev của admin (ADR 0003, SHIP-11). Đường phố khớp phường nên GHN không báo `From address conflict`.
    private const string PickupProvinceName = "Hà Nội";
    private const string PickupWardName = "Phường Hoàn Kiếm";
    private const string PickupStreetLine = "12 Tràng Tiền";
    private const string PickupSenderName = "Kho Pickory Test";
    private const string PickupSenderPhone = "0901234567";

    [LiveGhnFact]
    // SHIP-11: tạo rồi hủy đơn bằng đúng request mà `ShippingService` dựng, và đơn trên sandbox phải mang địa chỉ lấy hàng của cấu hình.
    public async Task Sandbox_CreateThenCancelOrder_WithShippingServiceRequest_KeepsConfiguredPickupAddress()
    {
        var client = CreateSandboxClient();
        var (receiverProvince, receiverWard) = await PickActiveWardAsync(client);
        var pickupWard = await FindPickupWardAsync(client);

        // Dựng request bằng hàm của `ShippingService`, không tự ghép tay, để test không lệch với code thật.
        var clientOrderCode = $"ship11-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var shipmentRequest = new CreateShipmentRequest(
            clientOrderCode,
            new ShippingRecipient(
                "Pickory Test",
                "0900000000",
                new ShippingAddress(receiverProvince.Code, receiverProvince.Name, receiverWard.Code, receiverWard.Name, "1 Duong Thu")),
            new Parcel(500, 15, 10, 5),
            [new ShipmentLine("Hang thu", 1)],
            CodAmount: 0,
            InsuredValue: 0,
            Note: "Don thu SHIP-11");
        var receiverCatalogWard = new AddressWard
        {
            Code = receiverWard.Code,
            ProvinceCode = receiverProvince.Code,
            Name = receiverWard.Name,
            Province = new AddressProvince { Code = receiverProvince.Code, Name = receiverProvince.Name },
        };
        var settings = new ShippingSettings
        {
            Id = "singleton",
            SenderName = PickupSenderName,
            SenderPhone = PickupSenderPhone,
            ProvinceName = PickupProvinceName,
            WardName = pickupWard.Name,
            Line = PickupStreetLine,
        };
        var createRequest = ShippingService.BuildCreateOrderRequest(shipmentRequest, "0900000000", receiverCatalogWard, settings, clientOrderCode);

        string? createdOrderCode = null;
        try
        {
            var created = await client.CreateOrderAsync(createRequest, CancellationToken.None);
            createdOrderCode = created.OrderCode;
            Assert.False(string.IsNullOrWhiteSpace(created.OrderCode));

            // Đọc chi tiết thô vì `GhnOrderDetail` không mang các trường địa chỉ lấy hàng.
            var pickup = await ReadPickupAddressAsync(created.OrderCode);
            Assert.Equal(PickupSenderName, pickup.GetProperty("from_name").GetString());
            Assert.Equal(PickupSenderPhone, pickup.GetProperty("from_phone").GetString());
            // GHN ghi địa chỉ hai cấp vào `from_address_v2` và mã phường vào `from_ward_id_v2`. Thiếu cờ thì hai trường này là rỗng và 0.
            Assert.Equal(PickupStreetLine, pickup.GetProperty("from_address_v2").GetString());
            Assert.Equal(pickupWard.Id, pickup.GetProperty("from_ward_id_v2").GetInt64());
            Assert.True(pickup.GetProperty("is_new_from_address").GetBoolean());

            await client.CancelOrderAsync(created.OrderCode, GhnCancelReason.Other, "Test hợp đồng SHIP-11", CancellationToken.None);

            var detailAfterCancel = await client.GetOrderDetailAsync(created.OrderCode, CancellationToken.None);
            Assert.Equal("cancel", detailAfterCancel.Status);
            createdOrderCode = null;
        }
        finally
        {
            // Hủy trong `finally` để test lỗi giữa chừng không để lại đơn sống trên sandbox.
            try
            {
                // Tạo đơn hết thời gian chờ thì `createdOrderCode` còn `null`, nhưng GHN có thể đã tạo đơn. Tra theo mã khách, vì API này chỉ trả đơn chưa hủy.
                var orderCodeToCancel = createdOrderCode ?? await FindLiveOrderCodeByClientCodeAsync(clientOrderCode);
                if (orderCodeToCancel is not null)
                {
                    await client.CancelOrderAsync(orderCodeToCancel, GhnCancelReason.Other, "Dọn đơn test SHIP-11", CancellationToken.None);
                }
            }
            catch (Exception cleanupError)
            {
                _output.WriteLine($"Không dọn được đơn {createdOrderCode ?? clientOrderCode}: {cleanupError.Message}");
            }
        }
    }

    [LiveGhnFact]
    // SHIP-12: API xem trước nhận địa chỉ hợp lệ, từ chối địa chỉ nhận có tên đường không thuộc phường, và không tạo đơn nào.
    public async Task Sandbox_PreviewOrder_AcceptsValidAddressAndRejectsMismatchedStreetWithoutCreatingOrder()
    {
        var client = CreateSandboxClient();
        var pickupWard = await FindPickupWardAsync(client);
        var (province, validWard) = await FindWardByNameAsync(client, "Hồ Chí Minh", "Phường Thủ Đức");
        var (_, mismatchedWard) = await FindWardByNameAsync(client, "Hồ Chí Minh", "Phường Bến Thành");
        var stamp = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";

        // Địa chỉ hợp lệ: phố `1 Duong Thu` thuộc `Phường Thủ Đức` theo kết quả thử của ADR 0003.
        var validCode = $"ship12-valid-{stamp}";
        var validRequest = BuildShippingServiceRequest(validCode, province, validWard, "1 Duong Thu", pickupWard, PickupStreetLine);
        var preview = await client.PreviewOrderAsync(validRequest, CancellationToken.None);
        Assert.True(preview.TotalFee > 0);

        // Địa chỉ nhận bị từ chối: phố `1 Nguyen Hue` không thuộc `Phường Bến Thành`. GHN báo `To address conflict`.
        var mismatchedCode = $"ship12-to-{stamp}";
        var mismatchedRequest = BuildShippingServiceRequest(mismatchedCode, province, mismatchedWard, "1 Nguyen Hue", pickupWard, PickupStreetLine);
        var toRejection = await Assert.ThrowsAsync<ShippingRejectedException>(
            () => client.PreviewOrderAsync(mismatchedRequest, CancellationToken.None));
        Assert.Equal(ShippingRejectionKind.ToAddressMismatch, toRejection.Kind);
        Assert.Contains("tên đường không khớp với phường/xã", toRejection.Message);

        // Địa chỉ lấy hàng bị từ chối: cùng phố `1 Nguyen Hue` đặt ở `Phường Hoàn Kiếm`. GHN báo `From address conflict`.
        var pickupCode = $"ship12-from-{stamp}";
        var pickupRequest = BuildShippingServiceRequest(pickupCode, province, validWard, "1 Duong Thu", pickupWard, "1 Nguyen Hue");
        var fromRejection = await Assert.ThrowsAsync<ShippingRejectedException>(
            () => client.PreviewOrderAsync(pickupRequest, CancellationToken.None));
        Assert.Equal(ShippingRejectionKind.FromAddressMismatch, fromRejection.Kind);

        // API xem trước không được tạo đơn. Tra theo từng mã khách để chắc không có đơn sống nào cần hủy.
        foreach (var clientOrderCode in new[] { validCode, mismatchedCode, pickupCode })
        {
            var leftover = await FindLiveOrderCodeByClientCodeAsync(clientOrderCode);
            if (leftover is not null)
            {
                await client.CancelOrderAsync(leftover, GhnCancelReason.Other, "Dọn đơn test SHIP-12", CancellationToken.None);
            }

            Assert.Null(leftover);
        }
    }

    // Dựng request bằng hàm của `ShippingService`, giống test SHIP-11, để request xem trước không lệch với request thật.
    private static GhnCreateOrderRequest BuildShippingServiceRequest(
        string clientOrderCode,
        GhnProvince receiverProvince,
        GhnWard receiverWard,
        string receiverLine,
        GhnWard pickupWard,
        string pickupLine)
    {
        var shipmentRequest = new CreateShipmentRequest(
            clientOrderCode,
            new ShippingRecipient(
                "Pickory Test",
                "0900000000",
                new ShippingAddress(receiverProvince.Code, receiverProvince.Name, receiverWard.Code, receiverWard.Name, receiverLine)),
            new Parcel(500, 15, 10, 5),
            [new ShipmentLine("Hang thu", 1)],
            CodAmount: 0,
            InsuredValue: 0,
            Note: "Don thu SHIP-12");
        var receiverCatalogWard = new AddressWard
        {
            Code = receiverWard.Code,
            ProvinceCode = receiverProvince.Code,
            Name = receiverWard.Name,
            Province = new AddressProvince { Code = receiverProvince.Code, Name = receiverProvince.Name },
        };
        var settings = new ShippingSettings
        {
            Id = "singleton",
            SenderName = PickupSenderName,
            SenderPhone = PickupSenderPhone,
            ProvinceName = PickupProvinceName,
            WardName = pickupWard.Name,
            Line = pickupLine,
        };
        return ShippingService.BuildCreateOrderRequest(shipmentRequest, "0900000000", receiverCatalogWard, settings, clientOrderCode);
    }

    // Tìm phường theo tên trong tỉnh có tên chứa `provinceName`. Thiếu phường là lỗi môi trường, nên báo rõ thay vì bỏ qua.
    private static async Task<(GhnProvince Province, GhnWard Ward)> FindWardByNameAsync(
        GhnHttpClient client, string provinceName, string wardName)
    {
        var provinces = await client.GetProvincesAsync(CancellationToken.None);
        var province = provinces.FirstOrDefault(candidate => candidate.Name.Contains(provinceName) && candidate.Status == 1);
        Assert.NotNull(province);
        var wards = await client.GetWardsAsync(province.Code, 0, 500, CancellationToken.None);
        var ward = wards.FirstOrDefault(candidate => candidate.Name == wardName && candidate.Status == 1);
        Assert.NotNull(ward);
        return (province, ward);
    }

    // Tìm phường lấy hàng theo tên trong danh mục thật. Thiếu phường là lỗi môi trường, nên báo rõ thay vì bỏ qua.
    private static async Task<GhnWard> FindPickupWardAsync(GhnHttpClient client)
    {
        var provinces = await client.GetProvincesAsync(CancellationToken.None);
        var province = provinces.FirstOrDefault(candidate => candidate.Name == PickupProvinceName && candidate.Status == 1);
        Assert.NotNull(province);
        var wards = await client.GetWardsAsync(province.Code, 0, 500, CancellationToken.None);
        var ward = wards.FirstOrDefault(candidate => candidate.Name == PickupWardName && candidate.Status == 1);
        Assert.NotNull(ward);
        return ward;
    }

    // Gọi thẳng API chi tiết đơn của GHN bằng token của sandbox, rồi trả phần `data` để test đọc các trường `from_*`.
    private static async Task<JsonElement> ReadPickupAddressAsync(string orderCode)
    {
        var data = await GetRawOrderDataAsync($"/shiip/public-api/v2/shipping-order/detail?order_code={Uri.EscapeDataString(orderCode)}");
        return data ?? throw new InvalidOperationException($"GHN không trả chi tiết cho đơn {orderCode}.");
    }

    // Tra đơn chưa hủy theo mã khách (ADR 0003, tiêu chí 3). Trả `null` khi GHN không có đơn nào mang mã đó.
    private static async Task<string?> FindLiveOrderCodeByClientCodeAsync(string clientOrderCode)
    {
        var data = await GetRawOrderDataAsync(
            $"/shiip/public-api/v2/shipping-order/detail-by-client-code?client_order_code={Uri.EscapeDataString(clientOrderCode)}");
        return data is { } order && order.TryGetProperty("order_code", out var orderCode) ? orderCode.GetString() : null;
    }

    // GET thẳng lên GHN bằng token của sandbox. Trả `null` khi GHN từ chối, vì lúc đó không có đơn để đọc.
    private static async Task<JsonElement?> GetRawOrderDataAsync(string pathAndQuery)
    {
        var token = PickoryWebApplicationFactory.ResolveEnvironmentValue("GHN_TOKEN");
        var shopId = PickoryWebApplicationFactory.ResolveEnvironmentValue("GHN_SHOP_ID");
        var apiBase = PickoryWebApplicationFactory.ResolveEnvironmentValue("GHN_API_BASE") ?? "https://dev-online-gateway.ghn.vn";
        using var httpClient = new HttpClient { BaseAddress = new Uri(apiBase), Timeout = TimeSpan.FromSeconds(60) };
        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        request.Headers.TryAddWithoutValidation("Token", token);
        request.Headers.TryAddWithoutValidation("ShopId", shopId);
        using var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data.Clone()
            : null;
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
