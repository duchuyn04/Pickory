using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-12: kiểm địa chỉ nhận bằng API xem trước của GHN. Hàm không tạo vận đơn, không ghi DB, và không ném lỗi khi GHN gặp sự cố.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentAddressCheckTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly RecordingGhnClient _ghn = new(new FakeGhnClient());
    private readonly ListLogger<ShippingService> _logger = new();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
    }

    public Task DisposeAsync() => ShipmentTestSetup.ResetAsync(factory);

    private async Task<AddressCheckResult> CheckAsync(ShippingAddress address)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _ghn, _logger, new RecordingEventDispatcher());
        return await service.CheckDeliveryAddressAsync(address, QuoteTestSetup.SampleParcel, CancellationToken.None);
    }

    // Client giả nằm trong bộ ghi lời gọi. Test đặt chế độ lỗi và đếm đơn qua đây.
    private FakeGhnClient Fake => (FakeGhnClient)_ghn.Inner;

    // Dòng địa chỉ có chuỗi đánh dấu của bản giả, nên bản giả báo `To address conflict`.
    private readonly ShippingAddress _rejectedAddress = QuoteTestSetup.ActiveAddress with
    {
        Line = $"1 Nguyen Hue {FakeGhnClient.RejectedAddressMarker}",
    };

    [Fact]
    public async Task Check_AddressAcceptedByGhn_ReturnsValidAndCreatesNothing()
    {
        var result = await CheckAsync(QuoteTestSetup.ActiveAddress);

        Assert.Equal(AddressCheckResult.Valid, result);
        Assert.Single(_ghn.PreviewOrderRequests);
        // Kiểm địa chỉ không được tạo vận đơn trên GHN và không được ghi gì vào DB.
        Assert.Empty(_ghn.CreateOrderRequests);
        Assert.Equal(0, Fake.CreatedOrderCount);
        Assert.Empty(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Empty(await ShipmentTestSetup.ReadEventsAsync(factory));
    }

    [Fact]
    public async Task Check_StreetNotInWard_ReturnsRejectedWithStreetReasonAndCreatesNothing()
    {
        var result = await CheckAsync(_rejectedAddress);

        Assert.Equal(AddressCheckResult.Rejected(AddressRejectionReason.StreetNotInWard), result);
        Assert.Empty(_ghn.CreateOrderRequests);
        Assert.Equal(0, Fake.CreatedOrderCount);
        Assert.Empty(await ShipmentTestSetup.ReadShipmentsAsync(factory));
    }

    [Fact]
    public async Task Check_WardNoLongerInCatalog_ReturnsRejectedWithoutCallingGhn()
    {
        // Phường 1003649 đã tắt trong danh mục đã seed.
        var inactiveWard = QuoteTestSetup.ActiveAddress with { WardCode = "1003649", WardName = "Phường đã tắt" };

        var result = await CheckAsync(inactiveWard);

        Assert.Equal(AddressCheckResult.Rejected(AddressRejectionReason.NotInCatalog), result);
        Assert.Equal(0, _ghn.TotalCallCount);
    }

    [Theory]
    [InlineData("Unavailable")]
    [InlineData("UnavailableAfterProcessing")]
    // Adapter đổi lỗi mạng, hết thời gian chờ và 5xx thành `ShippingUnavailableException`, và hàm kiểm không được ném lỗi này.
    // Tên chế độ lỗi là chuỗi vì `FakeGhnFailure` là `internal`.
    public async Task Check_GhnUnavailable_ReturnsUnverifiableWithoutThrowing(string failureName)
    {
        Fake.SetFailure(FakeGhnOperation.PreviewOrder, Enum.Parse<FakeGhnFailure>(failureName));

        var result = await CheckAsync(QuoteTestSetup.ActiveAddress);

        Assert.Equal(AddressCheckResult.Unverifiable, result);
        Assert.Empty(_ghn.CreateOrderRequests);
    }

    [Fact]
    public async Task Check_GhnRejectsForUnknownReason_ReturnsUnverifiable()
    {
        // Lý do lạ không chứng minh địa chỉ của khách sai, nên không được chặn khách đặt đơn.
        Fake.SetFailure(FakeGhnOperation.PreviewOrder, FakeGhnFailure.Rejected);

        var result = await CheckAsync(QuoteTestSetup.ActiveAddress);

        Assert.Equal(AddressCheckResult.Unverifiable, result);
    }

    [Fact]
    public async Task Check_PickupWardUnknownToGhn_ReturnsUnverifiableBecauseCustomerCannotFixIt()
    {
        await ShipmentTestSetup.UpdateSenderWardNameAsync(factory, "Phường Không Có");

        var result = await CheckAsync(QuoteTestSetup.ActiveAddress);

        Assert.Equal(AddressCheckResult.Unverifiable, result);
    }

    [Fact]
    public async Task Check_ShippingNotConfigured_ReturnsUnverifiableWithoutCallingGhn()
    {
        await ShipmentTestSetup.DeleteSettingsAsync(factory);

        var result = await CheckAsync(QuoteTestSetup.ActiveAddress);

        Assert.Equal(AddressCheckResult.Unverifiable, result);
        Assert.Equal(0, _ghn.TotalCallCount);
    }

    [Fact]
    public async Task Check_SendsSameAddressesAsCreateShipment()
    {
        await CheckAsync(QuoteTestSetup.ActiveAddress);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _ghn, _logger, new RecordingEventDispatcher());
            await service.CreateShipmentAsync(
                ShipmentTestSetup.SampleRequest with { To = ShipmentTestSetup.SampleRequest.To with { Address = QuoteTestSetup.ActiveAddress } },
                CancellationToken.None);
        }

        var preview = Assert.Single(_ghn.PreviewOrderRequests);
        var created = Assert.Single(_ghn.CreateOrderRequests);
        // Hai request gửi cùng địa chỉ, nên kết quả kiểm phản ánh đúng request tạo vận đơn, trừ khi GHN chập chờn (ADR 0003).
        Assert.Equal(created.ToAddress, preview.ToAddress);
        Assert.Equal(created.ToProvinceName, preview.ToProvinceName);
        Assert.Equal(created.ToWardName, preview.ToWardName);
        Assert.Equal(created.FromAddress, preview.FromAddress);
        Assert.Equal(created.FromProvinceName, preview.FromProvinceName);
        Assert.Equal(created.FromWardName, preview.FromWardName);
        Assert.Equal(created.IsNewFromAddress, preview.IsNewFromAddress);
        Assert.True(preview.IsNewToAddress);
    }

    [Fact]
    public async Task Check_ThenCreateWithSameAddress_BothSeeMarkerRule()
    {
        // Bản giả áp một luật địa chỉ cho cả hai hàm: địa chỉ kiểm bị từ chối thì tạo vận đơn cũng bị từ chối.
        var rejected = await CheckAsync(_rejectedAddress);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _ghn, _logger, new RecordingEventDispatcher());
        var request = ShipmentTestSetup.SampleRequest with { To = ShipmentTestSetup.SampleRequest.To with { Address = _rejectedAddress } };
        var error = await Assert.ThrowsAsync<Pickory.Modules.Shipping.Contracts.Errors.ShippingRejectedException>(
            () => service.CreateShipmentAsync(request, CancellationToken.None));

        Assert.Equal(AddressRejectionReason.StreetNotInWard, rejected.Reason);
        Assert.Equal(Pickory.Modules.Shipping.Contracts.Errors.ShippingRejectionKind.ToAddressMismatch, error.Kind);
    }
}
