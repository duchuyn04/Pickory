using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-03 lát 2: kiểm năm hàm còn lại về đường dẫn, method, thân gửi đi và cách đọc phản hồi.
// Các ca lỗi chung chạy theo tên hàm để mọi hàm qua cùng một bộ kiểm.
public sealed class GhnHttpClientOperationsTests
{
    private static readonly GhnFeeRequest FeeRequest = new(
        Weight: 1500, Length: 30, Width: 20, Height: 12, ServiceTypeId: 2, InsuranceValue: 2000000, CodValue: 500000, ToWardCode: "1003646");

    private static readonly GhnCreateOrderRequest CreateRequest = new(
        ToName: "Nguyễn Văn A", ToPhone: "0900000000", ToAddress: "12 Phố Test", ToProvinceName: "Hồ Chí Minh",
        ToWardName: "Phường Bến Nghé", ClientOrderCode: "ORD-1", Weight: 1500, Length: 30, Width: 20, Height: 12,
        ServiceTypeId: 2, PaymentTypeId: 1, CodAmount: 500000, InsuranceValue: 2000000, Content: "Điện thoại");

    // Thân `data` mà GHN trả khi thành công, theo tên hàm. Dùng cho các ca lỗi chung.
    private static readonly Dictionary<string, string> SuccessData = new()
    {
        ["GetWards"] = "[]",
        ["CalculateFee"] = """{"total":42000}""",
        ["CreateOrder"] = """{"order_code":"GHN1","total_fee":42000}""",
        ["CancelOrder"] = """[{"order_code":"GHN1","result":true,"message":"OK"}]""",
        ["GetOrderDetail"] = """{"order_code":"GHN1","status":"ready_to_pick"}""",
    };

    // Tên hàm cho các ca lỗi chung. Truyền tên thay vì delegate vì kiểu của adapter là `internal`.
    public static TheoryData<string> OperationNames => new("GetWards", "CalculateFee", "CreateOrder", "CancelOrder", "GetOrderDetail");

    private static Task Call(GhnHttpClient client, string operation) => operation switch
    {
        "GetWards" => client.GetWardsAsync("1000001", 0, 200, CancellationToken.None),
        "CalculateFee" => client.CalculateFeeAsync(FeeRequest, CancellationToken.None),
        "CreateOrder" => client.CreateOrderAsync(CreateRequest, CancellationToken.None),
        "CancelOrder" => client.CancelOrderAsync("GHN1", GhnCancelReason.Other, null, CancellationToken.None),
        "GetOrderDetail" => client.GetOrderDetailAsync("GHN1", CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Hàm không có trong bảng test."),
    };

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task Operation_SendsTokenAndShopIdHeaders(string operation)
    {
        var harness = new GhnClientHarness();
        harness.RespondOk(SuccessData[operation]);

        await Call(harness.Client, operation);

        var request = harness.Handler.Requests[0];
        Assert.Equal(GhnClientHarness.Token, request.Headers!["Token"]);
        Assert.Equal(GhnClientHarness.ShopId, request.Headers["ShopId"]);
        // Token không bao giờ nằm trong URL hay thân request, ở bất kỳ hàm nào.
        Assert.False(request.Url.Contains(GhnClientHarness.Token) || request.Body.Contains(GhnClientHarness.Token), operation);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task Operation_Http400_ThrowsRejected(string operation)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, "Từ chối " + operation);

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => Call(harness.Client, operation));

        Assert.Contains("Từ chối " + operation, exception.Message);
        // 400 không thử lại ở hàm nào: gửi lại cùng yêu cầu vẫn bị từ chối.
        Assert.Equal(1, harness.RequestCount);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task Operation_Http5xx_ThrowsUnavailable(string operation)
    {
        var harness = new GhnClientHarness();
        // Xếp hai phản hồi, vì báo phí thử lại một lần. Các hàm khác chỉ dùng phản hồi đầu.
        harness.RespondError(HttpStatusCode.InternalServerError);
        harness.RespondError(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => Call(harness.Client, operation));
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    // Handler không bao giờ trả lời nên chỉ việc adapter tự hết giờ mới kết thúc. Kết quả không phụ thuộc đồng hồ.
    public async Task Operation_Timeout_ThrowsUnavailable(string operation)
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueHang();
        harness.Handler.EnqueueHang();

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => Call(harness.Client, operation));
    }

    [Fact]
    public async Task GetWards_SendsProvinceOffsetAndLimitInQuery_AndParsesWards()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("""[{"_id":1003646,"name":"Phường Bến Nghé","type":"ward","parent_id":1000001,"status":1}]""");

        var wards = await harness.Client.GetWardsAsync("1000001", 200, 100, CancellationToken.None);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "https://ghn.test/shiip/public-api/v3/master-data/ward/all-by-province-id?province_id=1000001&offset=200&limit=100",
            request.Url);
        var ward = Assert.Single(wards);
        Assert.Equal("1003646", ward.Code);
        Assert.Equal(1000001, ward.ParentId);
        Assert.Equal("Phường Bến Nghé", ward.Name);
    }

    [Fact]
    // GHN trả HTTP 200 với `data: null` khi tỉnh không tồn tại. Đó là kết quả rỗng, không phải lỗi.
    public async Task GetWards_UnknownProvince_ReturnsEmptyList()
    {
        var harness = new GhnClientHarness();
        harness.Handler.EnqueueJson(HttpStatusCode.OK, """{"code":200,"message":"Success","data":null}""");

        var wards = await harness.Client.GetWardsAsync("999", 0, 200, CancellationToken.None);

        Assert.Empty(wards);
    }

    [Fact]
    // Mã tỉnh đi vào query string, nên ký tự đặc biệt phải được mã hóa để không chèn thêm tham số.
    public async Task GetWards_ProvinceIdIsEscapedInQuery()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("[]");

        await harness.Client.GetWardsAsync("1&offset=9", 0, 10, CancellationToken.None);

        Assert.Contains("province_id=1%26offset%3D9&offset=0&limit=10", harness.Handler.Requests[0].Url);
    }

    [Fact]
    public async Task CalculateFee_PostsParcelAndNewWardCode_AndReadsFee()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("""{"total":42000,"service_fee":38000,"insurance_fee":4000,"cod_fee":0}""");

        var fee = await harness.Client.CalculateFeeAsync(FeeRequest, CancellationToken.None);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://ghn.test/shiip/public-api/v2/shipping-order/fee", request.Url);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal(1500, root.GetProperty("weight").GetInt32());
        Assert.Equal(2, root.GetProperty("service_type_id").GetInt32());
        Assert.Equal(2000000, root.GetProperty("insurance_value").GetInt32());
        Assert.Equal(500000, root.GetProperty("cod_value").GetInt32());
        Assert.Equal("1003646", root.GetProperty("to_ward_code").GetString());
        // Không tự chọn mã quận hoặc huyện của danh mục cũ. Trường này không xuất hiện trong request.
        Assert.False(root.TryGetProperty("to_district_id", out _));
        Assert.Equal(new GhnFee(42000, 38000, 4000, 0), fee);
    }

    [Fact]
    public async Task CreateOrder_PostsTwoLevelAddressAndClientOrderCode_AndReadsOrderCode()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("""{"order_code":"LADH94","total_fee":42000,"expected_delivery_time":"2026-10-12T10:00:00Z"}""");

        var created = await harness.Client.CreateOrderAsync(CreateRequest, CancellationToken.None);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://ghn.test/shiip/public-api/v2/shipping-order/create", request.Url);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.True(root.GetProperty("is_new_to_address").GetBoolean());
        Assert.Equal("Hồ Chí Minh", root.GetProperty("to_province_name").GetString());
        Assert.Equal("Phường Bến Nghé", root.GetProperty("to_ward_name").GetString());
        Assert.Equal("ORD-1", root.GetProperty("client_order_code").GetString());
        Assert.Equal("CHOXEMHANGKHONGTHU", root.GetProperty("required_note").GetString());
        Assert.Equal(1, root.GetProperty("payment_type_id").GetInt32());
        // Địa chỉ hai cấp: không gửi tên quận hoặc huyện.
        Assert.False(root.TryGetProperty("to_district_name", out _));
        Assert.Equal("LADH94", created.OrderCode);
        Assert.Equal(42000, created.TotalFee);
    }

    [Fact]
    public async Task CancelOrder_PostsOrderCodesAndReason_AndCompletesWhenCancelled()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("""[{"order_code":"LADH94","result":true,"message":"OK"}]""");

        await harness.Client.CancelOrderAsync("LADH94", GhnCancelReason.Other, "Khách đổi ý", CancellationToken.None);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://ghn.test/shiip/public-api/v2/switch-status/cancel", request.Url);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("LADH94", root.GetProperty("order_codes")[0].GetString());
        Assert.Equal(1, root.GetProperty("order_codes").GetArrayLength());
        Assert.Equal("GHN-CANCEL-OTHER", root.GetProperty("reason_code").GetString());
        Assert.Equal("Khách đổi ý", root.GetProperty("reason").GetString());
    }

    [Fact]
    // Đơn đã lấy hàng không hủy được. GHN trả HTTP 200 với `result: false`, và phải thành lỗi từ chối.
    public async Task CancelOrder_ResultFalse_ThrowsRejectedWithItemMessage()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("""[{"order_code":"LADH94","result":false,"message":"Đơn đã lấy hàng"}]""");

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(
            () => harness.Client.CancelOrderAsync("LADH94", GhnCancelReason.Other, null, CancellationToken.None));

        Assert.Contains("Đơn đã lấy hàng", exception.Message);
        Assert.Contains(LogLevel.Warning, harness.Logger.RecordedLevels);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""[{"order_code":"KHAC","result":true,"message":"OK"}]""")]
    // Không có kết quả cho đúng mã đơn thì không biết đơn đã hủy chưa. Coi là phản hồi hỏng, không coi là đã hủy.
    public async Task CancelOrder_NoResultForOrderCode_ThrowsUnavailable(string dataJson)
    {
        var harness = new GhnClientHarness();
        harness.RespondOk(dataJson);

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.CancelOrderAsync("LADH94", GhnCancelReason.Other, null, CancellationToken.None));

        Assert.Contains(LogLevel.Error, harness.Logger.RecordedLevels);
    }

    [Fact]
    public async Task GetOrderDetail_SendsGetWithOrderCodeInQuery_AndReadsStatus()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("""{"order_code":"LADH94","client_order_code":"ORD-1","status":"picking","updated_date":"2026-10-09T08:00:00Z"}""");

        var detail = await harness.Client.GetOrderDetailAsync("LADH94", CancellationToken.None);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("https://ghn.test/shiip/public-api/v2/shipping-order/detail?order_code=LADH94", request.Url);
        Assert.Equal("LADH94", detail.OrderCode);
        Assert.Equal("ORD-1", detail.ClientOrderCode);
        Assert.Equal("picking", detail.Status);
    }
}
