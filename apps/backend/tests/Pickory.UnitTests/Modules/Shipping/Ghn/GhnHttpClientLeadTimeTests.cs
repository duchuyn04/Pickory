using System.Net;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-05 lát 4: adapter gọi API leadtime của GHN. Lời gọi này không thử lại và có timeout riêng.
public sealed class GhnHttpClientLeadTimeTests
{
    private static readonly GhnLeadTimeRequest Request = new(1000001, 1003646, 2, 1500, 30, 20, 12);

    [Fact]
    public async Task GetLeadTime_Success_ReturnsUnixSecondsAndSendsExpectedRequest()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("""{"leadtime":1792000000,"leadtime_order":{"from_estimate_date":"2026-10-14T17:00:00Z","to_estimate_date":"2026-10-15T17:00:00Z"}}""");

        var leadTime = await harness.Client.GetLeadTimeAsync(Request, CancellationToken.None);

        Assert.Equal(1792000000L, leadTime.Leadtime);
        var sent = Assert.Single(harness.Handler.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("https://ghn.test/shiip/public-api/v2/shipping-order/leadtime", sent.Url);
        Assert.Equal(GhnClientHarness.Token, sent.Headers!["Token"]);
        Assert.Equal(GhnClientHarness.ShopId, sent.Headers["ShopId"]);
        // Mã gửi dạng số JSON (không có dấu nháy) cùng cờ địa chỉ 2 cấp, giống request báo phí.
        Assert.Contains("\"is_new_to_address\":true", sent.Body);
        Assert.Contains("\"to_province_id_v2\":1000001", sent.Body);
        Assert.Contains("\"to_ward_id_v2\":1003646", sent.Body);
        Assert.DoesNotContain("to_ward_code", sent.Body);
        Assert.Contains("\"service_type_id\":2", sent.Body);
        Assert.Contains("\"weight\":1500", sent.Body);
        Assert.DoesNotContain("to_district_id", sent.Body);
    }

    [Fact]
    public async Task GetLeadTime_MissingLeadtimeField_ReturnsNullLeadtime()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("{}");

        var leadTime = await harness.Client.GetLeadTimeAsync(Request, CancellationToken.None);

        Assert.Null(leadTime.Leadtime);
    }

    [Fact]
    // Ngày giao chỉ là thông tin phụ. Lỗi 5xx không được thử lại để không kéo dài báo phí.
    public async Task GetLeadTime_Http5xx_CallsOnceAndThrowsUnavailable()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.InternalServerError);
        harness.RespondOk("""{"leadtime":1792000000}""");

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.GetLeadTimeAsync(Request, CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task GetLeadTime_NetworkError_CallsOnceAndThrowsUnavailable()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueException(new HttpRequestException("Mất kết nối"));
        harness.RespondOk("""{"leadtime":1792000000}""");

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.GetLeadTimeAsync(Request, CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public async Task GetLeadTime_Http400_ThrowsRejected()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, "Thiếu mã phường");

        await Assert.ThrowsAsync<ShippingRejectedException>(
            () => harness.Client.GetLeadTimeAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task GetLeadTime_MissingData_ThrowsUnavailable()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueJson(HttpStatusCode.OK, """{"code":200,"message":"Success","data":null}""");

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.GetLeadTimeAsync(Request, CancellationToken.None));
    }

    [Fact]
    // Timeout của leadtime ngắn hơn timeout báo phí, nên lời gọi treo bị hủy sớm hơn 5 giây mặc định.
    public async Task GetLeadTime_Hang_TimesOutWithLeadTimeTimeoutAndCallsOnce()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueHang();
        harness.RespondOk("""{"leadtime":1792000000}""");

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.GetLeadTimeAsync(Request, CancellationToken.None));

        Assert.Equal(1, harness.RequestCount);
    }

    [Fact]
    public void DefaultLeadTimeTimeout_IsShorterThanFeeTimeout()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), GhnHttpClient.DefaultLeadTimeTimeout);
        Assert.True(GhnHttpClient.DefaultLeadTimeTimeout < GhnHttpClient.DefaultTimeout);
    }
}
