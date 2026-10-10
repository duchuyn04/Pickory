using System.Net;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Shipping.Contracts.Errors;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-03 lát 1: lớp lỗi, quy đổi lỗi HTTP và header, kiểm bằng hàm lấy tỉnh/thành.
public sealed class GhnHttpClientProvincesTests
{
    private const string ProvincesJson = """[{"_id":1000001,"name":"Hồ Chí Minh","extension_names":["hcm"],"type":"province","parent_id":1,"status":1},{"_id":1000002,"name":"Đã tắt","status":2}]""";

    [Fact]
    public async Task GetProvinces_Success_ReturnsProvincesWithStringCode()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk(ProvincesJson);

        var provinces = await harness.Client.GetProvincesAsync(CancellationToken.None);

        Assert.Equal(2, provinces.Count);
        Assert.Equal("1000001", provinces[0].Code);
        Assert.Equal("Hồ Chí Minh", provinces[0].Name);
        // Đơn vị bị tắt vẫn được trả về. Job đồng bộ dựa vào `Status` để đánh dấu không còn dùng.
        Assert.Equal(2, provinces[1].Status);
    }

    [Fact]
    public async Task GetProvinces_SendsGetWithTokenAndShopIdHeaders_AndTokenNeverInUrl()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("[]");

        await harness.Client.GetProvincesAsync(CancellationToken.None);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("https://ghn.test/shiip/public-api/v3/master-data/province/all", request.Url);
        Assert.Equal(GhnClientHarness.Token, request.Headers!["Token"]);
        Assert.Equal(GhnClientHarness.ShopId, request.Headers["ShopId"]);
        Assert.DoesNotContain(GhnClientHarness.Token, request.Url);
    }

    [Fact]
    public async Task GetProvinces_Http400_ThrowsRejectedWithCarrierMessage_AndLogsWarning()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, "Địa chỉ không được phục vụ");

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(
            () => harness.Client.GetProvincesAsync(CancellationToken.None));

        Assert.Equal("SHIPPING_REJECTED", exception.Code);
        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Địa chỉ không được phục vụ", exception.Message);
        Assert.Contains(LogLevel.Warning, harness.Logger.RecordedLevels);
        Assert.DoesNotContain(LogLevel.Error, harness.Logger.RecordedLevels);
    }

    [Fact]
    // GHN có thể trả HTTP 200 nhưng `code` trong thân khác 200. Đó vẫn là từ chối.
    public async Task GetProvinces_Http200WithErrorCode_ThrowsRejected()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueJson(HttpStatusCode.OK, """{"code":400,"message":"Thiếu tham số","data":null}""");

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(
            () => harness.Client.GetProvincesAsync(CancellationToken.None));

        Assert.Contains("Thiếu tham số", exception.Message);
        Assert.Contains(LogLevel.Warning, harness.Logger.RecordedLevels);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    // Token sai hoặc IP không được phép là lỗi cấu hình của shop, nên ghi mức lỗi để người vận hành thấy.
    public async Task GetProvinces_Http401Or403_ThrowsUnavailable_AndLogsError(HttpStatusCode status)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(status, "Token không hợp lệ");

        var exception = await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.GetProvincesAsync(CancellationToken.None));

        Assert.Equal("SHIPPING_UNAVAILABLE", exception.Code);
        Assert.Equal(503, exception.StatusCode);
        Assert.Contains(LogLevel.Error, harness.Logger.RecordedLevels);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetProvinces_Http408_429_5xx_ThrowsUnavailable_AndLogsWarning(HttpStatusCode status)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(status);

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => harness.Client.GetProvincesAsync(CancellationToken.None));

        Assert.Contains(LogLevel.Warning, harness.Logger.RecordedLevels);
        Assert.DoesNotContain(LogLevel.Error, harness.Logger.RecordedLevels);
    }

    [Fact]
    public async Task GetProvinces_NetworkError_ThrowsUnavailable()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueException(new HttpRequestException("Không kết nối được"));

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => harness.Client.GetProvincesAsync(CancellationToken.None));

        Assert.Contains(LogLevel.Warning, harness.Logger.RecordedLevels);
    }

    [Fact]
    // Handler không bao giờ trả lời. Chỉ việc adapter tự hết giờ mới kết thúc lời gọi, nên test không phụ thuộc đồng hồ.
    public async Task GetProvinces_Timeout_ThrowsUnavailable()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueHang();

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => harness.Client.GetProvincesAsync(CancellationToken.None));

        Assert.Contains(LogLevel.Warning, harness.Logger.RecordedLevels);
    }

    [Fact]
    // Bên gọi hủy thì không phải lỗi của GHN. Phải ném lại để bên gọi biết mình đã hủy.
    public async Task GetProvinces_CancelledByCaller_RethrowsOperationCanceled()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueHang();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Client.GetProvincesAsync(cancellation.Token));
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("")]
    [InlineData("""{"code":200,"message":"Success"}""")]
    // Thân 2xx không đọc được hoặc thiếu `data` là lỗi phía GHN hoặc mạng trung gian, nên ghi mức lỗi.
    public async Task GetProvinces_UnreadableBody_ThrowsUnavailable_AndLogsError(string body)
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueJson(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => harness.Client.GetProvincesAsync(CancellationToken.None));

        Assert.Contains(LogLevel.Error, harness.Logger.RecordedLevels);
    }
}
