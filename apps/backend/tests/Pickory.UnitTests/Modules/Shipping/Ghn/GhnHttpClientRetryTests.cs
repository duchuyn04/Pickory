using System.Net;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-03 lát 3: chỉ báo phí được thử lại đúng 1 lần khi lỗi mạng, hết giờ hoặc 5xx, còn tạo đơn không thử lại.
// Mỗi test đếm số request mà handler nhận.
public sealed class GhnHttpClientRetryTests
{
    private static readonly GhnFeeRequest FeeRequest = new(1500, 30, 20, 12, 2, 0, 0, 1000001, 1003646);

    private static readonly GhnCreateOrderRequest CreateRequest = new(
        "Nguyễn Văn A", "0900000000", "12 Phố Test", "Hồ Chí Minh", "Phường Bến Nghé", "ORD-1",
        1500, 30, 20, 12, 2, 1, 0, 0, "Điện thoại");

    private const string FeeJson = """{"total":42000,"service_fee":42000,"insurance_fee":0,"cod_fee":0}""";

    [Fact]
    public async Task CalculateFee_Http5xxThenSuccess_CallsTwiceAndReturnsFee()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.ServiceUnavailable);
        harness.RespondOk(FeeJson);

        var fee = await harness.Client.CalculateFeeAsync(FeeRequest, CancellationToken.None);

        Assert.Equal(42000, fee.Total);
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task CalculateFee_Http5xxTwice_CallsExactlyTwiceThenThrowsUnavailable()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.InternalServerError);
        harness.RespondError(HttpStatusCode.InternalServerError);
        // Phản hồi thứ ba chỉ để chứng minh adapter không gọi lần thứ ba. Nếu gọi, `RequestCount` sẽ là 3.
        harness.RespondOk(FeeJson);

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.CalculateFeeAsync(FeeRequest, CancellationToken.None));

        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task CalculateFee_NetworkErrorThenSuccess_CallsTwice()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueException(new HttpRequestException("Mất kết nối"));
        harness.RespondOk(FeeJson);

        await harness.Client.CalculateFeeAsync(FeeRequest, CancellationToken.None);

        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task CalculateFee_TimeoutThenSuccess_CallsTwice()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueHang();
        harness.RespondOk(FeeJson);

        await harness.Client.CalculateFeeAsync(FeeRequest, CancellationToken.None);

        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    // Lần thử lại phải dựng request mới và vẫn mang đủ header, vì `HttpRequestMessage` không gửi lại được.
    public async Task CalculateFee_RetryRequestStillCarriesHeadersAndBody()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadGateway);
        harness.RespondOk(FeeJson);

        await harness.Client.CalculateFeeAsync(FeeRequest, CancellationToken.None);

        Assert.All(harness.Handler.Requests, request =>
        {
            Assert.Equal(GhnClientHarness.Token, request.Headers!["Token"]);
            Assert.Equal(GhnClientHarness.ShopId, request.Headers["ShopId"]);
            Assert.Contains("\"weight\":1500", request.Body);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    // Các mã này không tự qua khỏi nhờ gửi lại, hoặc gửi lại làm GHN quá tải thêm. Đúng 1 lần gọi.
    public async Task CalculateFee_NonRetryableStatus_CallsOnce(HttpStatusCode status)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(status);
        harness.RespondOk(FeeJson);

        await Assert.ThrowsAnyAsync<Pickory.Shared.Exceptions.AppException>(
            () => harness.Client.CalculateFeeAsync(FeeRequest, CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task CalculateFee_Http200WithErrorCode_CallsOnce()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueJson(HttpStatusCode.OK, """{"code":400,"message":"Sai tham số","data":null}""");
        harness.RespondOk(FeeJson);

        await Assert.ThrowsAsync<ShippingRejectedException>(
            () => harness.Client.CalculateFeeAsync(FeeRequest, CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task CreateOrder_Http5xx_CallsOnceAndThrowsUnavailable()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.InternalServerError);
        harness.RespondOk("""{"order_code":"GHN1","total_fee":1}""");

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.CreateOrderAsync(CreateRequest, CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task CreateOrder_Timeout_CallsOnceAndThrowsUnavailable()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueHang();
        harness.RespondOk("""{"order_code":"GHN1","total_fee":1}""");

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.CreateOrderAsync(CreateRequest, CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task CreateOrder_NetworkError_CallsOnceAndThrowsUnavailable()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueException(new HttpRequestException("Mất kết nối"));
        harness.RespondOk("""{"order_code":"GHN1","total_fee":1}""");

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.CreateOrderAsync(CreateRequest, CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
    }

    [Theory]
    [InlineData("GetProvinces")]
    [InlineData("GetWards")]
    [InlineData("CancelOrder")]
    [InlineData("GetOrderDetail")]
    // Hủy, tra chi tiết và lấy danh mục không thử lại. Webhook trả 503 để GHN tự gửi lại, và job đồng bộ chạy lại ở chu kỳ sau.
    public async Task OtherOperations_Http5xx_CallOnce(string operation)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.InternalServerError);
        harness.RespondOk("[]");

        Task Call() => operation switch
        {
            "GetProvinces" => harness.Client.GetProvincesAsync(CancellationToken.None),
            "GetWards" => harness.Client.GetWardsAsync("1000001", 0, 200, CancellationToken.None),
            "CancelOrder" => harness.Client.CancelOrderAsync("GHN1", GhnCancelReason.Other, null, CancellationToken.None),
            _ => harness.Client.GetOrderDetailAsync("GHN1", CancellationToken.None),
        };

        await Assert.ThrowsAsync<ShippingUnavailableException>(Call);

        Assert.Equal(1, harness.RequestCount);
    }
}
