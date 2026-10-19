using System.Net;
using System.Text.Json;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-12: lỗi địa chỉ của GHN thành thông báo tiếng Việt, không lặp tiền tố "Đơn vị vận chuyển từ chối yêu cầu".
// Các ca chạy qua `GhnHttpClient` để kiểm cả đường nhận lỗi HTTP 400 lẫn bước dịch.
public sealed class GhnRejectionMessageTests
{
    private const string CarrierPrefix = "Đơn vị vận chuyển từ chối yêu cầu";

    private static readonly GhnCreateOrderRequest OrderRequest = new(
        ToName: "Nguyễn Văn A", ToPhone: "0900000000", ToAddress: "12 Phố Test", ToProvinceName: "Hồ Chí Minh",
        ToWardName: "Phường Bến Nghé", ClientOrderCode: "ORD-1", Weight: 1500, Length: 30, Width: 20, Height: 12,
        ServiceTypeId: 2, PaymentTypeId: 1, CodAmount: 0, InsuranceValue: 0, Content: "Điện thoại");

    // Tên hàm cho các ca chạy chung. Hai hàm này cùng nhận địa chỉ nên cùng đi qua bước dịch.
    public static TheoryData<string> AddressOperations => new("CreateOrder", "PreviewOrder");

    public static TheoryData<string, string> AddressOperationsWithConflictMessages
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var operation in new[] { "CreateOrder", "PreviewOrder" })
            {
                // Hoa thường khác nhau, vì GHN có thể đổi chữ hoa và chữ thường.
                foreach (var message in new[] { "To address conflict", "to ADDRESS Conflict", "From address conflict", "FROM address conflict" })
                {
                    data.Add(operation, message);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string> PickupPlaceMessages => new()
    {
        { "CreateOrder", "Fail when get place name of from ward code: ward not found: Phường Không Có in province Hà Nội" },
        { "CreateOrder", "Fail when get place name of from ward code: province not found: Tỉnh Không Có" },
        { "PreviewOrder", "Fail when get place name of from ward code: ward not found: Phường Không Có in province Hà Nội" },
        { "PreviewOrder", "Fail when get place name of from ward code: province not found: Tỉnh Không Có" },
        // Khác chữ hoa và chữ thường.
        { "PreviewOrder", "FAIL WHEN GET PLACE NAME OF FROM WARD CODE: WARD NOT FOUND: Phường Không Có in province Hà Nội" },
    };

    private static Task Call(GhnHttpClient client, string operation) => operation switch
    {
        "CreateOrder" => client.CreateOrderAsync(OrderRequest, CancellationToken.None),
        "PreviewOrder" => client.PreviewOrderAsync(OrderRequest, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Hàm không có trong bảng test."),
    };

    [Theory]
    [MemberData(nameof(AddressOperationsWithConflictMessages))]
    public async Task AddressConflict_IsTranslatedWithoutCarrierPrefixOrEnglish(string operation, string carrierMessage)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, carrierMessage);

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => Call(harness.Client, operation));

        Assert.StartsWith("Đơn vị vận chuyển không nhận địa chỉ vì tên đường không khớp với phường/xã đã chọn.", exception.Message);
        // Thông báo nhắc kiểm cả địa chỉ nhận lẫn địa chỉ lấy hàng, vì GHN từng gắn nhãn sai cho hai địa chỉ này.
        Assert.Contains("địa chỉ nhận của đơn", exception.Message);
        Assert.Contains("địa chỉ lấy hàng trong cấu hình vận chuyển", exception.Message);
        Assert.DoesNotContain(CarrierPrefix, exception.Message);
        Assert.DoesNotContain("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ShippingRejectedException.ErrorCode, exception.Code);
        Assert.Equal(422, exception.StatusCode);
    }

    [Theory]
    [InlineData("CreateOrder", "To address conflict", ShippingRejectionKind.ToAddressMismatch)]
    [InlineData("PreviewOrder", "to address CONFLICT", ShippingRejectionKind.ToAddressMismatch)]
    [InlineData("CreateOrder", "From address conflict", ShippingRejectionKind.FromAddressMismatch)]
    [InlineData("PreviewOrder", "FROM ADDRESS CONFLICT", ShippingRejectionKind.FromAddressMismatch)]
    public async Task AddressConflict_KeepsKindSoCallersCanTellToFromFrom(
        string operation, string carrierMessage, ShippingRejectionKind expectedKind)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, carrierMessage);

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => Call(harness.Client, operation));

        Assert.Equal(expectedKind, exception.Kind);
    }

    [Theory]
    [MemberData(nameof(PickupPlaceMessages))]
    public async Task PickupPlaceNotFound_IsTranslatedToConfigurationHint(string operation, string carrierMessage)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, carrierMessage);

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => Call(harness.Client, operation));

        Assert.StartsWith("Địa chỉ lấy hàng trong cấu hình vận chuyển có tỉnh/thành hoặc phường/xã không còn trong danh mục", exception.Message);
        Assert.Contains("Cập nhật lại ở trang cấu hình vận chuyển.", exception.Message);
        Assert.DoesNotContain(CarrierPrefix, exception.Message);
        Assert.DoesNotContain("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ShippingRejectionKind.PickupPlaceNotInCatalog, exception.Kind);
        Assert.Equal(ShippingRejectedException.ErrorCode, exception.Code);
    }

    [Theory]
    [MemberData(nameof(AddressOperations))]
    public async Task OtherCarrierMessage_KeepsCurrentFormat(string operation)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, "Token hết hạn");

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => Call(harness.Client, operation));

        Assert.Equal("Đơn vị vận chuyển từ chối yêu cầu: Token hết hạn", exception.Message);
        Assert.Equal(ShippingRejectionKind.Other, exception.Kind);
    }

    [Theory]
    [MemberData(nameof(AddressOperations))]
    public async Task ToWardNotFoundOfRecipient_IsNotMistakenForPickupConfiguration(string operation)
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, "to ward not found");

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => Call(harness.Client, operation));

        // Chỉ lỗi của địa chỉ lấy hàng mới nhắc cập nhật cấu hình. Lỗi phường của địa chỉ nhận giữ cách báo hiện tại.
        Assert.Equal("Đơn vị vận chuyển từ chối yêu cầu: to ward not found", exception.Message);
        Assert.Equal(ShippingRejectionKind.Other, exception.Kind);
    }

    [Fact]
    public async Task AddressConflictInsideEnvelopeWithHttp200_IsTranslatedToo()
    {
        var harness = new GhnClientHarness();
        // GHN có thể trả HTTP 200 với `code` 400 trong thân. Đường này cũng phải qua bước dịch.
        harness.Handler.EnqueueJson(HttpStatusCode.OK, """{"code":400,"message":"To address conflict","data":null}""");

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(
            () => harness.Client.PreviewOrderAsync(OrderRequest, CancellationToken.None));

        Assert.Equal(ShippingRejectionKind.ToAddressMismatch, exception.Kind);
        Assert.DoesNotContain(CarrierPrefix, exception.Message);
    }

    [Fact]
    public async Task TranslatedMessage_NeverCarriesToken()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadRequest, $"To address conflict {GhnClientHarness.Token}");

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(
            () => harness.Client.PreviewOrderAsync(OrderRequest, CancellationToken.None));

        Assert.DoesNotContain(GhnClientHarness.Token, exception.ToString());
        Assert.DoesNotContain(harness.Logger.RecordedMessages, message => message.Contains(GhnClientHarness.Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewOrder_PostsSameBodyAsCreateToPreviewPath_AndReadsTotalFee()
    {
        var harness = new GhnClientHarness();
        harness.RespondOk("""{"order_code":"","sort_code":"GXT-M-11-00","total_fee":20900}""");

        var preview = await harness.Client.PreviewOrderAsync(OrderRequest, CancellationToken.None);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://ghn.test/shiip/public-api/v2/shipping-order/preview", request.Url);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("Phường Bến Nghé", body.RootElement.GetProperty("to_ward_name").GetString());
        Assert.True(body.RootElement.GetProperty("is_new_to_address").GetBoolean());
        Assert.Equal(20900, preview.TotalFee);
    }

    [Fact]
    public async Task PreviewOrder_Http5xx_ThrowsUnavailableWithoutRetry()
    {
        var harness = new GhnClientHarness();
        harness.RespondError(HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => harness.Client.PreviewOrderAsync(OrderRequest, CancellationToken.None));

        // Bên gọi coi "không kiểm được" là kết quả bình thường, nên adapter không thử lại để khỏi kéo dài lúc đặt đơn.
        Assert.Equal(1, harness.RequestCount);
    }
}
