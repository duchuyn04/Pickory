using System.Net;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Quotes;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-05 lát 3: bằng chứng cho tiêu chí "Retry đúng 1 lần", dùng `GhnHttpClient` thật với handler giả.
// Adapter (SHIP-03) giữ việc thử lại, nên service không được nhân số lần đó.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class QuoteRetryTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string FeePath = "/shiip/public-api/v2/shipping-order/fee";
    private const string FeeJson = """{"code":200,"message":"Success","data":{"total":42000,"service_fee":42000,"insurance_fee":0,"cod_fee":0}}""";
    private const string LeadTimePath = "/shiip/public-api/v2/shipping-order/leadtime";
    private const string LeadTimeJson = """{"code":200,"message":"Success","data":{"leadtime":1792000000}}""";
    private const string ErrorJson = """{"code":500,"message":"Lỗi máy chủ","data":null}""";

    private readonly RoutedHttpHandler _handler = new();
    private readonly ListLogger<ShippingService> _serviceLogger = new();

    public async Task InitializeAsync()
    {
        await QuoteTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: null);
    }

    public Task DisposeAsync() => QuoteTestSetup.ResetAsync(factory);

    private GhnHttpClient CreateGhnClient()
    {
        // Báo phí kèm một lời gọi ngày giao chạy song song. Ngày giao luôn thành công ở đây để chỉ có báo phí được kiểm.
        _handler.EnqueueJson(LeadTimePath, HttpStatusCode.OK, LeadTimeJson);
        var httpClient = new HttpClient(_handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://ghn.test"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new GhnHttpClient(
            httpClient, "tok-SECRET-9f3a71c2", "4455667", new ListLogger<GhnHttpClient>(), timeout: TimeSpan.FromMilliseconds(500));
    }

    private Task<Quote> QuoteAsync() => QuoteTestSetup.QuoteAsync(factory, CreateGhnClient(), _serviceLogger);

    [Fact]
    public async Task Quote_Http5xxThenSuccess_SendsExactlyTwoFeeRequestsAndReturnsFee()
    {
        _handler.EnqueueJson(FeePath, HttpStatusCode.ServiceUnavailable, ErrorJson);
        _handler.EnqueueJson(FeePath, HttpStatusCode.OK, FeeJson);
        // Phản hồi thứ ba chỉ để chứng minh không có lần gọi thứ ba. Nếu có, số request sẽ là 3.
        _handler.EnqueueJson(FeePath, HttpStatusCode.OK, FeeJson);

        var quote = await QuoteAsync();

        Assert.Equal(42_000, quote.Fee);
        Assert.Equal(2, _handler.RequestCount(FeePath));
    }

    [Fact]
    public async Task Quote_Http5xxTwice_SendsExactlyTwoFeeRequestsThenThrowsUnavailable()
    {
        _handler.EnqueueJson(FeePath, HttpStatusCode.InternalServerError, ErrorJson);
        _handler.EnqueueJson(FeePath, HttpStatusCode.InternalServerError, ErrorJson);
        _handler.EnqueueJson(FeePath, HttpStatusCode.OK, FeeJson);

        var exception = await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync());

        Assert.Equal("SHIPPING_UNAVAILABLE", exception.Code);
        Assert.Equal(2, _handler.RequestCount(FeePath));
    }

    [Fact]
    public async Task Quote_NetworkErrorTwice_SendsExactlyTwoFeeRequestsThenThrowsUnavailable()
    {
        _handler.EnqueueException(FeePath, new HttpRequestException("Mất kết nối"));
        _handler.EnqueueException(FeePath, new HttpRequestException("Mất kết nối"));
        _handler.EnqueueJson(FeePath, HttpStatusCode.OK, FeeJson);

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteAsync());

        Assert.Equal(2, _handler.RequestCount(FeePath));
    }
}
