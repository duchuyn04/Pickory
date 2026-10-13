using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với kiểu của bên gọi.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-07 lát 5: tra vận đơn theo mã, và log không chứa dữ liệu cá nhân của người nhận.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentLookupAndLogTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string CreatePath = "/shiip/public-api/v2/shipping-order/create";
    private const string CancelPath = "/shiip/public-api/v2/switch-status/cancel";
    private const string Token = "tok-SECRET-9f3a71c2";

    private readonly RecordingGhnClient _ghn = new(new FakeGhnClient());
    private readonly RecordingEventDispatcher _events = new();
    private readonly ListLogger<ShippingService> _serviceLogger = new();
    private readonly ListLogger<GhnHttpClient> _adapterLogger = new();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
    }

    public Task DisposeAsync() => ShipmentTestSetup.ResetAsync(factory);

    private GhnHttpClient CreateHttpClient(RoutedHttpHandler handler)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://ghn.test"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new GhnHttpClient(httpClient, Token, "4455667", _adapterLogger, timeout: TimeSpan.FromMilliseconds(500));
    }

    private async Task<T> WithServiceAsync<T>(IGhnClient ghn, Func<ShippingService, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, ghn, _serviceLogger, _events);
        return await action(service);
    }

    [Fact]
    public async Task Get_ExistingShipment_ReturnsContractValues()
    {
        var created = await WithServiceAsync(_ghn, service => service.CreateShipmentAsync(
            ShipmentTestSetup.SampleRequest, CancellationToken.None));

        var found = await WithServiceAsync(_ghn, service => service.GetShipmentAsync(created.Id, CancellationToken.None));

        Assert.Equal(created, found);
        Assert.Equal(ShipmentStatus.PendingPickup, found!.Status);
        Assert.Equal("ship07-ref-1", found.Ref);
    }

    [Fact]
    // COM-14: nhân viên xem chi tiết đơn nên tra vận đơn theo mã đơn (`ref`), không cần biết mã vận đơn nội bộ.
    public async Task GetByRef_ExistingShipment_ReturnsContractValues()
    {
        var created = await WithServiceAsync(_ghn, service => service.CreateShipmentAsync(
            ShipmentTestSetup.SampleRequest, CancellationToken.None));

        var found = await WithServiceAsync(_ghn, service =>
            service.GetShipmentByRefAsync("ship07-ref-1", CancellationToken.None));

        Assert.Equal(created, found);
        Assert.Equal("ship07-ref-1", found!.Ref);
    }

    [Fact]
    // Đơn chưa có vận đơn thì trả `null`, không ném lỗi.
    public async Task GetByRef_UnknownRef_ReturnsNull()
    {
        var found = await WithServiceAsync(_ghn, service =>
            service.GetShipmentByRefAsync("ship-khong-ton-tai", CancellationToken.None));

        Assert.Null(found);
    }

    [Fact]
    // Vận đơn đã hủy bị đổi `ref` sang `{ref}#cancelled#{n}`, nên đơn không còn vận đơn còn hiệu lực.
    public async Task GetByRef_CancelledShipment_ReturnsNull()
    {
        var created = await WithServiceAsync(_ghn, service => service.CreateShipmentAsync(
            ShipmentTestSetup.CreateRequest("ship14-cancelled"), CancellationToken.None));
        await WithServiceAsync(_ghn, async service =>
        {
            await service.CancelShipmentAsync(created.Id, CancellationToken.None);
            return true;
        });

        var found = await WithServiceAsync(_ghn, service =>
            service.GetShipmentByRefAsync("ship14-cancelled", CancellationToken.None));

        Assert.Null(found);
    }

    [Fact]
    // Mã đơn rỗng là lỗi lập trình của bên gọi, giống `GetShipmentAsync` với mã vận đơn rỗng.
    public async Task GetByRef_BlankRef_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => WithServiceAsync(_ghn, service =>
            service.GetShipmentByRefAsync(" ", CancellationToken.None)));
    }

    [Fact]
    public async Task Get_UnknownShipment_ReturnsNull()
    {
        var found = await WithServiceAsync(_ghn, service =>
            service.GetShipmentAsync(Guid.NewGuid().ToString(), CancellationToken.None));

        Assert.Null(found);
    }

    [Fact]
    // Chạy cả đường thành công lẫn đường lỗi, rồi quét mọi dòng log của service và adapter.
    public async Task Logs_NeverContainRecipientPhoneNameOrAddress()
    {
        var handler = new RoutedHttpHandler();
        handler.EnqueueJson(
            CreatePath, HttpStatusCode.OK, "{\"code\":200,\"message\":\"Success\",\"data\":{\"order_code\":\"GHNLOG1\",\"total_fee\":31000}}");
        handler.EnqueueJson(
            CreatePath, HttpStatusCode.BadRequest, "{\"code\":400,\"message\":\"Số điện thoại không hợp lệ\",\"data\":null}");
        handler.EnqueueException(CreatePath, new HttpRequestException("Mất kết nối"));
        handler.EnqueueJson(
            CancelPath,
            HttpStatusCode.OK,
            "{\"code\":200,\"message\":\"Success\",\"data\":[{\"order_code\":\"GHNLOG1\",\"result\":true,\"message\":\"OK\"}]}");
        var ghn = CreateHttpClient(handler);

        // Thành công, GHN từ chối, và lỗi mạng khi tạo.
        var created = await WithServiceAsync(ghn, service => service.CreateShipmentAsync(
            ShipmentTestSetup.CreateRequest("ship07-log-1"), CancellationToken.None));
        await Assert.ThrowsAsync<ShippingRejectedException>(() => WithServiceAsync(ghn, service =>
            service.CreateShipmentAsync(ShipmentTestSetup.CreateRequest("ship07-log-2"), CancellationToken.None)));
        await Assert.ThrowsAsync<ShippingUnavailableException>(() => WithServiceAsync(ghn, service =>
            service.CreateShipmentAsync(ShipmentTestSetup.CreateRequest("ship07-log-3"), CancellationToken.None)));

        // Hủy thành công, rồi tạo với số điện thoại sai để đi vào nhánh từ chối trước khi gọi GHN.
        await WithServiceAsync(ghn, async service =>
        {
            await service.CancelShipmentAsync(created.Id, CancellationToken.None);
            return true;
        });
        await Assert.ThrowsAsync<ShippingRejectedException>(() => WithServiceAsync(ghn, service =>
            service.CreateShipmentAsync(
                ShipmentTestSetup.CreateRequest("ship07-log-4", phone: "0123456789"), CancellationToken.None)));

        var lines = _serviceLogger.Entries.Select(entry => entry.Message)
            .Concat(_adapterLogger.Entries.Select(entry => entry.Message))
            .ToList();
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            Assert.DoesNotContain("0987654321", line);
            Assert.DoesNotContain("987654321", line);
            Assert.DoesNotContain("0123456789", line);
            Assert.DoesNotContain("Nguyễn Văn Khách", line);
            Assert.DoesNotContain("12 Phố Test", line);
            Assert.DoesNotContain(Token, line);
        }
    }
}
