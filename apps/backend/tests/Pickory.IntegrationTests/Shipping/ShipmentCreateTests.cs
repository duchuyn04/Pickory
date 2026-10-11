using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với kiểu của bên gọi.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-07 lát 1: tạo vận đơn thành công lưu `shipments`, một dòng `shipment_events` và phát một sự kiện sau commit.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentCreateTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly RecordingGhnClient _ghn = new(new FakeGhnClient());
    private readonly RecordingEventDispatcher _events = new();
    private readonly ListLogger<ShippingService> _logger = new();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
    }

    public Task DisposeAsync() => ShipmentTestSetup.ResetAsync(factory);

    private async Task<Shipment> CreateAsync(CreateShipmentRequest request)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _ghn, _logger, _events);
        return await service.CreateShipmentAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task Create_Success_StoresShipmentOneEventAndPublishesEventWithoutPreviousStatus()
    {
        var shipment = await CreateAsync(ShipmentTestSetup.SampleRequest);

        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal(shipment.Id, stored.Id);
        Assert.Equal("ship07-ref-1", stored.Ref);
        Assert.Equal(shipment.TrackingCode, stored.TrackingCode);
        Assert.StartsWith("FAKE", stored.TrackingCode);
        // `FakeGhnClient`: 20.000 đ, cộng 2.000 đ cho 3 khối 500 g, cộng bảo hiểm 0,5% của 1.000.000 đ là 5.000 đ.
        Assert.Equal(31_000, stored.Fee);
        Assert.Equal(31_000, shipment.Fee);
        Assert.Equal(250_000, stored.CodAmount);
        Assert.Equal(new Pickory.Modules.Shipping.Contracts.Parcels.Parcel(1500, 30, 20, 12), stored.Parcel);
        Assert.Equal(ShipmentStatus.PendingPickup, stored.Status);
        Assert.Equal("ready_to_pick", stored.RawStatus);
        Assert.Equal(ShipmentStatus.PendingPickup, shipment.Status);

        var history = Assert.Single(await ShipmentTestSetup.ReadEventsAsync(factory));
        Assert.Equal(stored.Id, history.ShipmentId);
        Assert.Equal(ShipmentStatus.PendingPickup, history.Status);
        Assert.Equal("ready_to_pick", history.RawStatus);
        Assert.Equal(ShipmentEventSource.Manual, history.Source);

        var published = Assert.IsType<ShipmentStatusChangedEvent>(Assert.Single(_events.Published));
        Assert.Equal(stored.Id, published.ShipmentId);
        Assert.Equal("ship07-ref-1", published.Ref);
        Assert.Equal(ShipmentStatus.PendingPickup, published.Status);
        Assert.Null(published.PreviousStatus);
    }

    [Fact]
    // `Ref` đã có vận đơn còn hiệu lực thì trả vận đơn đó. GHN không nhận lời gọi và không phát sự kiện lần hai.
    public async Task Create_RefAlreadyHasShipment_ReturnsExistingWithoutCallingGhnOrPublishing()
    {
        var first = await CreateAsync(ShipmentTestSetup.SampleRequest);
        var callsAfterFirst = _ghn.TotalCallCount;

        var second = await CreateAsync(ShipmentTestSetup.SampleRequest);

        Assert.Equal(first, second);
        Assert.Equal(callsAfterFirst, _ghn.TotalCallCount);
        Assert.Single(_events.Published);
        Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
    }

    [Fact]
    // GHN đã tạo đơn nhưng phản hồi mất giữa đường. Lần 1 không lưu gì, lần 2 nhận lại đúng đơn cũ.
    public async Task Create_NetworkErrorAfterGhnCreated_StoresNothingThenRetryReturnsSameOrder()
    {
        var fake = (FakeGhnClient)_ghn.Inner;
        fake.SetFailure(FakeGhnOperation.CreateOrder, FakeGhnFailure.UnavailableAfterProcessing);

        var failure = await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => CreateAsync(ShipmentTestSetup.SampleRequest));

        Assert.Equal("SHIPPING_UNAVAILABLE", failure.Code);
        Assert.Empty(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Empty(_events.Published);
        Assert.Equal(1, fake.CreatedOrderCount);

        fake.ClearFailures();
        var shipment = await CreateAsync(ShipmentTestSetup.SampleRequest);

        // Hai lần gọi gửi cùng `client_order_code`, nên GHN giả không tạo đơn thứ hai.
        Assert.Equal(2, _ghn.CreateOrderRequests.Count);
        Assert.Equal("ship07-ref-1", _ghn.CreateOrderRequests[0].ClientOrderCode);
        Assert.Equal("ship07-ref-1", _ghn.CreateOrderRequests[1].ClientOrderCode);
        Assert.Equal(1, fake.CreatedOrderCount);
        Assert.Equal("FAKE000001", shipment.TrackingCode);
        Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
    }

    [Fact]
    public async Task Create_SendsCatalogNamesClampedInsuranceNormalizedPhoneAndConfiguredServiceType()
    {
        await ShipmentTestSetup.UpdateServiceTypeAsync(factory, 5);

        await CreateAsync(ShipmentTestSetup.CreateRequest(
            "ship07-ref-2", phone: "+84 987.654.321", insuredValue: 12_000_000, note: "Gọi trước khi giao"));

        var sent = Assert.Single(_ghn.CreateOrderRequests);
        // Tên trong danh mục, không phải tên trong tham số của bên gọi.
        Assert.Equal("Hồ Chí Minh", sent.ToProvinceName);
        Assert.Equal("Phường Bến Nghé", sent.ToWardName);
        Assert.Equal("0987654321", sent.ToPhone);
        Assert.Equal("Nguyễn Văn Khách", sent.ToName);
        Assert.Equal("12 Phố Test", sent.ToAddress);
        Assert.Equal(5_000_000, sent.InsuranceValue);
        Assert.Equal(5, sent.ServiceTypeId);
        Assert.Equal(250_000, sent.CodAmount);
        Assert.Equal(1, sent.PaymentTypeId);
        Assert.Equal("Điện thoại Pickory x2, Ốp lưng x1", sent.Content);
        Assert.Equal("Gọi trước khi giao", sent.Note);
        Assert.True(sent.IsNewToAddress);
        Assert.Equal("CHOXEMHANGKHONGTHU", sent.RequiredNote);
        // Địa chỉ lấy hàng lấy từ cấu hình của admin.
        Assert.Equal("Kho Pickory", sent.FromName);
        Assert.Equal("0912345678", sent.FromPhone);
        Assert.Equal("12 Phố Kho", sent.FromAddress);
        Assert.Equal("Hà Nội", sent.FromProvinceName);
        Assert.Equal("Phường Ba Đình", sent.FromWardName);
        Assert.True(sent.IsNewFromAddress);
    }

    [Fact]
    // Kiểm giá trị thật trong JSON gửi GHN, không chỉ giá trị trong record. Dùng `GhnHttpClient` thật với handler giả.
    public async Task Create_RequestJson_CarriesRequiredNoteNewAddressFlagsAndPaymentType()
    {
        const string createPath = "/shiip/public-api/v2/shipping-order/create";
        var handler = new RoutedHttpHandler();
        handler.EnqueueJson(
            createPath,
            System.Net.HttpStatusCode.OK,
            "{\"code\":200,\"message\":\"Success\",\"data\":{\"order_code\":\"GHNREAL1\",\"total_fee\":31000}}");
        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://ghn.test"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var ghn = new GhnHttpClient(httpClient, "tok-SECRET-9f3a71c2", "4455667", new ListLogger<GhnHttpClient>());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = QuoteTestSetup.CreateService(scope.ServiceProvider, ghn, _logger, _events);
            await service.CreateShipmentAsync(ShipmentTestSetup.SampleRequest, CancellationToken.None);
        }

        var body = Assert.Single(handler.Bodies(createPath));
        // Đọc lại JSON thay vì so chuỗi, vì bộ mã hóa JSON thoát ký tự có dấu thành `\uXXXX`.
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("CHOXEMHANGKHONGTHU", root.GetProperty("required_note").GetString());
        Assert.True(root.GetProperty("is_new_to_address").GetBoolean());
        Assert.True(root.GetProperty("is_new_from_address").GetBoolean());
        Assert.Equal(1, root.GetProperty("payment_type_id").GetInt32());
        Assert.Equal("Hồ Chí Minh", root.GetProperty("to_province_name").GetString());
        Assert.Equal("Phường Bến Nghé", root.GetProperty("to_ward_name").GetString());
        Assert.Equal("ship07-ref-1", root.GetProperty("client_order_code").GetString());
        Assert.Equal("0987654321", root.GetProperty("to_phone").GetString());
        Assert.Equal("Phường Ba Đình", root.GetProperty("from_ward_name").GetString());
        Assert.False(root.TryGetProperty("to_district_name", out _));
    }

    // Gửi qua `GhnHttpClient` thật với handler giả và trả JSON GHN đã nhận.
    private async Task<System.Text.Json.JsonDocument> CreateAndReadSentJsonAsync(int serviceTypeId)
    {
        const string createPath = "/shiip/public-api/v2/shipping-order/create";
        await ShipmentTestSetup.UpdateServiceTypeAsync(factory, serviceTypeId);
        var handler = new RoutedHttpHandler();
        handler.EnqueueJson(
            createPath,
            System.Net.HttpStatusCode.OK,
            "{\"code\":200,\"message\":\"Success\",\"data\":{\"order_code\":\"GHNREAL1\",\"total_fee\":31000}}");
        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://ghn.test"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var ghn = new GhnHttpClient(httpClient, "tok-SECRET-9f3a71c2", "4455667", new ListLogger<GhnHttpClient>());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = QuoteTestSetup.CreateService(scope.ServiceProvider, ghn, _logger, _events);
            await service.CreateShipmentAsync(ShipmentTestSetup.SampleRequest, CancellationToken.None);
        }

        return System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Bodies(createPath)));
    }

    [Fact]
    // Gói 5 tính phí theo từng kiện nên GHN bắt buộc `items`. Một dòng mang số đo của `Parcel` mẫu 1500 g, 30 x 20 x 12 cm.
    public async Task Create_HeavyGoodsServiceType_JsonCarriesOneItemWithParcelMeasures()
    {
        using var json = await CreateAndReadSentJsonAsync(serviceTypeId: 5);

        var items = json.RootElement.GetProperty("items");
        var item = Assert.Single(items.EnumerateArray());
        Assert.Equal(1, item.GetProperty("quantity").GetInt32());
        Assert.Equal(1500, item.GetProperty("weight").GetInt32());
        Assert.Equal(30, item.GetProperty("length").GetInt32());
        Assert.Equal(20, item.GetProperty("width").GetInt32());
        Assert.Equal(12, item.GetProperty("height").GetInt32());
        Assert.Equal("Điện thoại Pickory x2, Ốp lưng x1", item.GetProperty("name").GetString());
        // Gói 5 vẫn gửi `content` để mô tả hàng hóa ở cấp đơn.
        Assert.Equal("Điện thoại Pickory x2, Ốp lưng x1", json.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Create_LightGoodsServiceType_JsonHasNoItemsField()
    {
        using var json = await CreateAndReadSentJsonAsync(serviceTypeId: 2);

        Assert.False(json.RootElement.TryGetProperty("items", out _));
        Assert.Equal("Điện thoại Pickory x2, Ốp lưng x1", json.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    // Mã vận đơn GHN trùng với vận đơn của `Ref` khác không phải tình huống bấm tạo hai lần. Lỗi phải đi lên bên gọi.
    public async Task Create_TrackingCodeBelongsToOtherRef_ThrowsAndStoresNothingNew()
    {
        // `FakeGhnClient` cấp mã đầu tiên là `FAKE000001`. Vận đơn chèn sẵn đã giữ mã đó.
        await ShipmentTestSetup.InsertShipmentAsync(factory, "ship07-other-ref", "FAKE000001");

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => CreateAsync(ShipmentTestSetup.SampleRequest));

        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal("ship07-other-ref", stored.Ref);
        Assert.Empty(await ShipmentTestSetup.ReadEventsAsync(factory));
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Create_NotConfigured_ThrowsNotConfiguredAndDoesNotCallGhn()
    {
        await ShipmentTestSetup.DeleteSettingsAsync(factory);

        await Assert.ThrowsAsync<ShippingNotConfiguredException>(() => CreateAsync(ShipmentTestSetup.SampleRequest));

        Assert.Equal(0, _ghn.TotalCallCount);
    }

    [Theory]
    [InlineData("9999", "1000001")]
    [InlineData("1003649", "1000001")]
    [InlineData("1004001", "1000001")]
    public async Task Create_AddressNotInCatalog_ThrowsRejectedAndDoesNotCallGhn(string wardCode, string provinceCode)
    {
        var request = ShipmentTestSetup.SampleRequest with
        {
            To = ShipmentTestSetup.SampleRequest.To with
            {
                Address = ShipmentTestSetup.SampleRequest.To.Address with { WardCode = wardCode, ProvinceCode = provinceCode },
            },
        };

        await Assert.ThrowsAsync<ShippingRejectedException>(() => CreateAsync(request));

        Assert.Equal(0, _ghn.TotalCallCount);
        Assert.Empty(await ShipmentTestSetup.ReadShipmentsAsync(factory));
    }

    [Fact]
    public async Task Create_CodAmountAboveGhnLimit_ThrowsRejectedAndDoesNotCallGhn()
    {
        await Assert.ThrowsAsync<ShippingRejectedException>(
            () => CreateAsync(ShipmentTestSetup.CreateRequest("ship07-ref-3", codAmount: 50_000_001)));

        Assert.Equal(0, _ghn.TotalCallCount);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("0123456789")]
    [InlineData("khong-phai-so")]
    public async Task Create_InvalidPhone_ThrowsRejectedAndDoesNotCallGhn(string phone)
    {
        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(
            () => CreateAsync(ShipmentTestSetup.CreateRequest("ship07-ref-4", phone: phone)));

        Assert.Equal("SHIPPING_REJECTED", exception.Code);
        Assert.Equal(0, _ghn.TotalCallCount);
    }

    [Fact]
    public async Task Create_ProgrammingErrors_ThrowArgumentExceptionAndDoNotCallGhn()
    {
        var valid = ShipmentTestSetup.SampleRequest;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => CreateAsync(valid with { Ref = " " }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => CreateAsync(valid with { Items = [] }));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => CreateAsync(valid with { Items = [new ShipmentLine("Hàng", 0)] }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => CreateAsync(valid with { CodAmount = -1 }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => CreateAsync(valid with { InsuredValue = -1 }));

        Assert.Equal(0, _ghn.TotalCallCount);
    }
}
