using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Quotes;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-05 lát 4: lỗi của leadtime không được làm hỏng báo phí vì ngày giao chỉ là thông tin phụ.
// Nhóm đầu dùng `FakeGhnClient` có chế độ lỗi, nhóm sau dùng `GhnHttpClient` thật để kiểm timeout và hủy.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class QuoteLeadTimeTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string FeePath = "/shiip/public-api/v2/shipping-order/fee";
    private const string LeadTimePath = "/shiip/public-api/v2/shipping-order/leadtime";
    private const string FeeJson = """{"code":200,"message":"Success","data":{"total":42000,"service_fee":42000,"insurance_fee":0,"cod_fee":0}}""";
    private const string LeadTimeJson = """{"code":200,"message":"Success","data":{"leadtime":1792000000}}""";

    private readonly FakeGhnClient _fake = new();
    private readonly RoutedHttpHandler _handler = new();
    private readonly ListLogger<ShippingService> _logger = new();

    public async Task InitializeAsync()
    {
        await QuoteTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: null);
    }

    public Task DisposeAsync() => QuoteTestSetup.ResetAsync(factory);

    private Task<Quote> QuoteWithFakeAsync() =>
        QuoteTestSetup.QuoteAsync(factory, _fake, _logger);

    private Task<Quote> QuoteWithHttpAsync(TimeSpan leadTimeTimeout)
    {
        var httpClient = new HttpClient(_handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://ghn.test"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var ghn = new GhnHttpClient(
            httpClient,
            "tok-SECRET-9f3a71c2",
            "4455667",
            new ListLogger<GhnHttpClient>(),
            timeout: TimeSpan.FromSeconds(5),
            leadTimeTimeout: leadTimeTimeout);
        return QuoteTestSetup.QuoteAsync(factory, ghn, _logger);
    }

    [Fact]
    public async Task Quote_LeadTimeSucceeds_ReturnsFeeAndExpectedDeliveryDate()
    {
        var quote = await QuoteWithFakeAsync();

        Assert.Equal(new DateOnly(2026, 10, 15), quote.ExpectedDeliveryDate);
        Assert.True(quote.Fee > 0);
    }

    [Fact]
    public async Task Quote_LeadTimeRequest_UsesSameWardServiceTypeAndParcelAsFee()
    {
        var recording = new RecordingGhnClient(_fake);

        await QuoteTestSetup.QuoteAsync(factory, recording, _logger);

        var request = Assert.Single(recording.LeadTimeRequests);
        Assert.Equal(1000001L, request.ToProvinceIdV2);
        Assert.Equal(1003646L, request.ToWardIdV2);
        Assert.True(request.IsNewToAddress);
        Assert.Equal(2, request.ServiceTypeId);
        Assert.Equal(1500, request.Weight);
        Assert.Equal(30, request.Length);
        Assert.Equal(20, request.Width);
        Assert.Equal(12, request.Height);
    }

    [Theory]
    [InlineData("Unavailable")]
    [InlineData("Rejected")]
    // Leadtime lỗi thì phí vẫn trả, ngày giao là `null`, và log ghi cảnh báo để người vận hành thấy.
    // Tên lỗi là chuỗi vì kiểu `FakeGhnFailure` là `internal`, còn phương thức test là `public`.
    public async Task Quote_LeadTimeFails_ReturnsFeeWithNullDateAndLogsWarning(string failureName)
    {
        _fake.SetFailure(FakeGhnOperation.GetLeadTime, Enum.Parse<FakeGhnFailure>(failureName));

        var quote = await QuoteWithFakeAsync();

        Assert.True(quote.Fee > 0);
        Assert.Null(quote.ExpectedDeliveryDate);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Quote_FeeFailsAndLeadTimeSucceeds_ThrowsFeeError()
    {
        _fake.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Unavailable);

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => QuoteWithFakeAsync());
    }

    [Fact]
    public async Task Quote_FeeRejectedAndLeadTimeFails_ThrowsFeeRejection()
    {
        _fake.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Rejected);
        _fake.SetFailure(FakeGhnOperation.GetLeadTime, FakeGhnFailure.Unavailable);

        await Assert.ThrowsAsync<ShippingRejectedException>(() => QuoteWithFakeAsync());
    }

    [Fact]
    public async Task Quote_LeadTimeResponseMissingDate_ReturnsFeeWithNullDate()
    {
        _handler.EnqueueJson(FeePath, HttpStatusCode.OK, FeeJson);
        _handler.EnqueueJson(LeadTimePath, HttpStatusCode.OK, """{"code":200,"message":"Success","data":{}}""");

        var quote = await QuoteWithHttpAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(42_000, quote.Fee);
        Assert.Null(quote.ExpectedDeliveryDate);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-86400")]
    // Số giây không dương sẽ thành ngày 1970 nếu đem đổi. Phải trả `null`, vẫn giữ phí và ghi cảnh báo.
    public async Task Quote_LeadTimeNotPositive_ReturnsFeeWithNullDateAndLogsWarning(string leadTimeSeconds)
    {
        _handler.EnqueueJson(FeePath, HttpStatusCode.OK, FeeJson);
        _handler.EnqueueJson(
            LeadTimePath,
            HttpStatusCode.OK,
            "{\"code\":200,\"message\":\"Success\",\"data\":{\"leadtime\":" + leadTimeSeconds + "}}");

        var quote = await QuoteWithHttpAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(42_000, quote.Fee);
        Assert.Null(quote.ExpectedDeliveryDate);
        Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    // Leadtime 5xx không được thử lại: đúng 1 request leadtime, trong khi phí vẫn về bình thường.
    public async Task Quote_LeadTimeHttp5xx_IsNotRetriedAndFeeStillReturned()
    {
        _handler.EnqueueJson(FeePath, HttpStatusCode.OK, FeeJson);
        _handler.EnqueueJson(LeadTimePath, HttpStatusCode.InternalServerError, """{"code":500,"message":"Lỗi","data":null}""");
        _handler.EnqueueJson(LeadTimePath, HttpStatusCode.OK, LeadTimeJson);

        var quote = await QuoteWithHttpAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(42_000, quote.Fee);
        Assert.Null(quote.ExpectedDeliveryDate);
        Assert.Equal(1, _handler.RequestCount(LeadTimePath));
        Assert.Equal(1, _handler.RequestCount(FeePath));
    }

    [Fact]
    // Leadtime treo thì báo phí chỉ chờ tới timeout riêng (300 ms), không chờ 5 giây của báo phí.
    // Hủy token dừng lời gọi treo nên test không phụ thuộc tốc độ máy, và mốc 3 giây bắt lỗi dùng nhầm timeout 5 giây.
    public async Task Quote_LeadTimeHangs_FeeIsNotDelayedBeyondLeadTimeTimeout()
    {
        _handler.EnqueueJson(FeePath, HttpStatusCode.OK, FeeJson);
        _handler.EnqueueHang(LeadTimePath);
        var stopwatch = Stopwatch.StartNew();

        var quote = await QuoteWithHttpAsync(TimeSpan.FromMilliseconds(300));

        stopwatch.Stop();
        Assert.Equal(42_000, quote.Fee);
        Assert.Null(quote.ExpectedDeliveryDate);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Báo phí chờ {stopwatch.Elapsed}.");
    }

    [Fact]
    // Báo phí bị từ chối thì leadtime còn treo phải bị hủy ngay. Timeout leadtime đặt 30 giây để chỉ có việc hủy mới cứu được test.
    public async Task Quote_FeeFailsWhileLeadTimeHangs_CancelsLeadTimeAndThrowsFeeError()
    {
        _handler.EnqueueJson(FeePath, HttpStatusCode.BadRequest, """{"code":400,"message":"Địa chỉ không phục vụ","data":null}""");
        _handler.EnqueueHang(LeadTimePath);
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<ShippingRejectedException>(() => QuoteWithHttpAsync(TimeSpan.FromSeconds(30)));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Lỗi báo phí chờ {stopwatch.Elapsed}.");
    }
}
