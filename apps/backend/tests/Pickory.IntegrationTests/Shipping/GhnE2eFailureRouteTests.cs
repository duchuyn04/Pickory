using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Quotes;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.FallbackFees;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-14: route `PUT` và `DELETE /api/e2e/ghn/failures/calculate-fee` bật và tắt lỗi báo phí của `FakeGhnClient` cho Playwright.
// Route làm hỏng báo phí mà không cần đăng nhập, nên host thường và host có token GHN không được có route này.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class GhnE2eFailureRouteTests(IntegrationDatabaseFixture database) : IAsyncLifetime
{
    private const string FailurePath = "/api/e2e/ghn/failures/calculate-fee";

    private readonly List<IDisposable> _disposables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        using var cleanupFactory = new PickoryWebApplicationFactory(database);
        await QuoteTestSetup.ResetAsync(cleanupFactory);
    }

    // Host chạy như `dotnet run` thường: `Program.cs` không map route E2E.
    private WebApplicationFactory<Program> CreateRegularHostFactory()
    {
        var factory = new PickoryWebApplicationFactory(database);
        _disposables.Add(factory);
        return factory;
    }

    // Host mô phỏng `e2e:start` với client GHN giả và thiếu `GHN_TOKEN`.
    private WebApplicationFactory<Program> CreateE2eFactory()
    {
        var factory = new PickoryWebApplicationFactory(database).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter, E2eShippingRoutesStartupFilter>()));
        _disposables.Add(factory);
        return factory;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static HttpRequestMessage FailureRequest(HttpMethod method, bool withOrigin = true)
    {
        var request = new HttpRequestMessage(method, FailurePath);
        if (withOrigin)
        {
            request.Headers.Add("Origin", "http://localhost:5173");
        }

        return request;
    }

    private static async Task SeedAsync(WebApplicationFactory<Program> factory, bool fallbackEnabled)
    {
        await QuoteTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
        if (!fallbackEnabled)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        // Đủ chín giá. Đơn Hà Nội tới Hồ Chí Minh đi tuyến liên vùng với gói 1500 g: 40.000 + 7.000 = 47.000 đ.
        shipping.FallbackFees.Add(new ShippingFallbackFee
        {
            Id = FallbackFeeStore.SingletonId,
            Enabled = true,
            SameProvinceBaseFee = 20_000,
            SameProvinceBaseWeightGrams = 1000,
            SameProvinceStepFee = 5_000,
            SameRegionBaseFee = 30_000,
            SameRegionBaseWeightGrams = 1000,
            SameRegionStepFee = 6_000,
            CrossRegionBaseFee = 40_000,
            CrossRegionBaseWeightGrams = 1000,
            CrossRegionStepFee = 7_000,
            UpdatedAt = QuoteTestSetup.SyncedAt,
        });
        await shipping.SaveChangesAsync();
    }

    private static async Task<Quote> QuoteAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<IShippingService>();
        return await shipping.QuoteAsync(
            QuoteTestSetup.ActiveAddress, QuoteTestSetup.SampleParcel, codAmount: 0, insuredValue: 0, CancellationToken.None);
    }

    [Fact]
    // Host thường không chạy `e2e:start`, nên cửa bật lỗi báo phí không được tồn tại và báo phí vẫn qua GHN.
    public async Task Route_RegularHost_Returns404ForPutAndDelete_AndQuoteStillUsesGhn()
    {
        var factory = CreateRegularHostFactory();
        using var client = CreateClient(factory);
        await SeedAsync(factory, fallbackEnabled: false);

        var put = await client.SendAsync(FailureRequest(HttpMethod.Put));
        var delete = await client.SendAsync(FailureRequest(HttpMethod.Delete));

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(QuoteSource.Ghn, (await QuoteAsync(factory)).Source);
    }

    [Fact]
    // Có `GHN_TOKEN` thì API dùng client GHN thật, nên route không được map dù DI của test vẫn giữ client giả.
    public async Task Route_HostWithGhnToken_Returns404_AndFakeClientIsNotBroken()
    {
        var factory = new ConfiguredGhnWebApplicationFactory(database).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter, E2eShippingRoutesStartupFilter>()));
        _disposables.Add(factory);
        using var client = CreateClient(factory);

        var put = await client.SendAsync(FailureRequest(HttpMethod.Put));
        var delete = await client.SendAsync(FailureRequest(HttpMethod.Delete));

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        // Client giả chưa bị đặt lỗi: gọi báo phí trực tiếp vẫn thành công.
        var fake = factory.Services.GetRequiredService<FakeGhnClient>();
        var fee = await fake.CalculateFeeAsync(
            new GhnFeeRequest(1500, 30, 20, 12, 2, 0, 0, 1000001, 1003646), CancellationToken.None);
        Assert.True(fee.Total > 0);
    }

    [Fact]
    // Chế độ E2E, bảng phí dự phòng tắt: bật lỗi thì báo phí trả `SHIPPING_UNAVAILABLE`.
    public async Task Route_E2eMode_PutThenQuoteWithoutFallbackTable_ThrowsUnavailable()
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);
        await SeedAsync(factory, fallbackEnabled: false);

        var put = await client.SendAsync(FailureRequest(HttpMethod.Put));

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var exception = await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync(factory));
        Assert.Equal("SHIPPING_UNAVAILABLE", exception.Code);
    }

    [Fact]
    // Chế độ E2E, bảng phí dự phòng bật: bật lỗi thì báo phí trả phí dự phòng, tắt lỗi thì trả lại phí GHN.
    public async Task Route_E2eMode_PutThenDelete_SwitchesQuoteBetweenFallbackAndGhn()
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);
        await SeedAsync(factory, fallbackEnabled: true);

        var put = await client.SendAsync(FailureRequest(HttpMethod.Put));
        var duringFailure = await QuoteAsync(factory);
        var delete = await client.SendAsync(FailureRequest(HttpMethod.Delete));
        var afterRecovery = await QuoteAsync(factory);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(QuoteSource.Fallback, duringFailure.Source);
        Assert.Equal(47_000, duringFailure.Fee);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(QuoteSource.Ghn, afterRecovery.Source);
    }

    [Fact]
    // Chỉ tắt lỗi báo phí. Lỗi của hàm khác mà test đã bật vẫn giữ nguyên.
    public async Task Route_E2eMode_Delete_KeepsFailuresOfOtherOperations()
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);
        var fake = factory.Services.GetRequiredService<FakeGhnClient>();
        fake.SetFailure(FakeGhnOperation.GetLeadTime, FakeGhnFailure.Unavailable);
        await client.SendAsync(FailureRequest(HttpMethod.Put));

        var delete = await client.SendAsync(FailureRequest(HttpMethod.Delete));

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        await Assert.ThrowsAsync<ShippingUnavailableException>(() =>
            fake.GetLeadTimeAsync(new GhnLeadTimeRequest(1000001, 1003646, 2, 1500, 30, 20, 12), CancellationToken.None));
        fake.ClearFailures();
    }

    [Fact]
    // Route không nằm trong danh sách miễn `Origin`, nên request thiếu `Origin` bị actor middleware chặn.
    public async Task Route_E2eMode_WithoutOrigin_Returns403AndDoesNotBreakFee()
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);
        await SeedAsync(factory, fallbackEnabled: false);

        var put = await client.SendAsync(FailureRequest(HttpMethod.Put, withOrigin: false));

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(QuoteSource.Ghn, (await QuoteAsync(factory)).Source);
    }
}
