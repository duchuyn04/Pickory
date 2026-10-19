using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-03 lát 5: client giả dùng khi thiếu token. Dữ liệu cố định, xác định, không gọi mạng, và chống tạo trùng như GHN.
public sealed class FakeGhnClientTests
{
    private static GhnCreateOrderRequest OrderRequest(string clientOrderCode, int weight = 1500, int insurance = 2000000) => new(
        "Nguyễn Văn A", "0900000000", "12 Phố Test", "Hồ Chí Minh", "Phường Bến Nghé", clientOrderCode,
        weight, 30, 20, 12, 2, 1, 0, insurance, "Điện thoại");

    [Fact]
    public async Task GetProvinces_ReturnsFixedListIncludingInactiveProvince()
    {
        var client = new FakeGhnClient();

        var first = await client.GetProvincesAsync(CancellationToken.None);
        var second = await client.GetProvincesAsync(CancellationToken.None);

        Assert.True(first.Count >= 2);
        Assert.Equal(first, second);
        Assert.Contains(first, province => province.Status == 1);
        // Job đồng bộ phải đánh dấu đơn vị bị tắt, nên dữ liệu giả có ít nhất một đơn vị như vậy.
        Assert.Contains(first, province => province.Status != 1);
        Assert.All(first, province => Assert.False(string.IsNullOrWhiteSpace(province.Name)));
    }

    [Fact]
    public async Task GetWards_ReturnsWardsOfProvince_WithPagingAndInactiveWard()
    {
        var client = new FakeGhnClient();
        var provinces = await client.GetProvincesAsync(CancellationToken.None);
        var provinceWithWards = provinces.First(province => province.Status == 1);

        var all = await client.GetWardsAsync(provinceWithWards.Code, 0, 200, CancellationToken.None);
        var firstPage = await client.GetWardsAsync(provinceWithWards.Code, 0, 2, CancellationToken.None);
        var secondPage = await client.GetWardsAsync(provinceWithWards.Code, 2, 2, CancellationToken.None);

        Assert.True(all.Count >= 3);
        Assert.All(all, ward => Assert.Equal(provinceWithWards.Id, ward.ParentId));
        Assert.Contains(all, ward => ward.Status != 1);
        Assert.Equal(all.Take(2), firstPage);
        Assert.Equal(all.Skip(2).Take(2), secondPage);
    }

    [Fact]
    // GHN trả danh sách rỗng cho tỉnh không tồn tại, và bản giả làm giống vậy.
    public async Task GetWards_UnknownProvince_ReturnsEmpty()
    {
        var client = new FakeGhnClient();

        Assert.Empty(await client.GetWardsAsync("khong-co", 0, 200, CancellationToken.None));
    }

    [Fact]
    // Công thức cố định: 20.000 đồng cộng 2.000 đồng cho mỗi 500 g bắt đầu, cộng bảo hiểm 0,5% giá trị khai.
    public async Task CalculateFee_UsesFixedFormula()
    {
        var client = new FakeGhnClient();

        var fee = await client.CalculateFeeAsync(new GhnFeeRequest(1500, 30, 20, 12, 2, 2000000, 0, 1000001, 1003646), CancellationToken.None);

        Assert.Equal(new GhnFee(Total: 36000, ServiceFee: 26000, InsuranceFee: 10000, CodFee: 0), fee);
    }

    [Fact]
    // GHN từ chối mã phường mới không có trong danh mục (`mã phường mới không tồn tại`). Bản giả phải từ chối giống vậy.
    public async Task CalculateFee_WardNotInCatalog_ThrowsRejected()
    {
        var client = new FakeGhnClient();

        await Assert.ThrowsAsync<ShippingRejectedException>(
            () => client.CalculateFeeAsync(new GhnFeeRequest(1500, 30, 20, 12, 2, 0, 0, 1000001, 99999999), CancellationToken.None));
    }

    [Fact]
    public async Task GetLeadTime_WardNotInCatalog_ThrowsRejected()
    {
        var client = new FakeGhnClient();

        await Assert.ThrowsAsync<ShippingRejectedException>(
            () => client.GetLeadTimeAsync(new GhnLeadTimeRequest(1000001, 99999999, 2, 1, 1, 1, 1), CancellationToken.None));
    }

    [Fact]
    // Thiếu cờ `is_new_to_address` thì GHN đọc mã theo danh mục cũ và báo `to ward not found`.
    public async Task CalculateFee_WithoutNewAddressFlag_ThrowsRejected()
    {
        var client = new FakeGhnClient();
        var request = new GhnFeeRequest(1500, 30, 20, 12, 2, 0, 0, 1000001, 1003646) { IsNewToAddress = false };

        await Assert.ThrowsAsync<ShippingRejectedException>(() => client.CalculateFeeAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CalculateFee_PartialHalfKilogramRoundsUp()
    {
        var client = new FakeGhnClient();

        var fee = await client.CalculateFeeAsync(new GhnFeeRequest(501, 1, 1, 1, 2, 0, 0, 1000001, 1003646), CancellationToken.None);

        Assert.Equal(24000, fee.ServiceFee);
    }

    [Fact]
    public async Task CreateOrder_SameClientOrderCode_ReturnsSameOrderCodeWithoutCreatingSecond()
    {
        var client = new FakeGhnClient();

        var first = await client.CreateOrderAsync(OrderRequest("ORD-1"), CancellationToken.None);
        var again = await client.CreateOrderAsync(OrderRequest("ORD-1"), CancellationToken.None);
        var other = await client.CreateOrderAsync(OrderRequest("ORD-2"), CancellationToken.None);

        Assert.Equal(first.OrderCode, again.OrderCode);
        Assert.NotEqual(first.OrderCode, other.OrderCode);
        Assert.Equal(36000, first.TotalFee);
    }

    [Fact]
    public async Task CreateOrder_ThenGetDetail_ReturnsReadyToPickWithClientOrderCode()
    {
        var client = new FakeGhnClient();
        var created = await client.CreateOrderAsync(OrderRequest("ORD-1"), CancellationToken.None);

        var detail = await client.GetOrderDetailAsync(created.OrderCode, CancellationToken.None);

        Assert.Equal(created.OrderCode, detail.OrderCode);
        Assert.Equal("ORD-1", detail.ClientOrderCode);
        Assert.Equal("ready_to_pick", detail.Status);
    }

    [Fact]
    public async Task CancelOrder_ChangesStatusToCancel_AndSecondCancelIsRejected()
    {
        var client = new FakeGhnClient();
        var created = await client.CreateOrderAsync(OrderRequest("ORD-1"), CancellationToken.None);

        await client.CancelOrderAsync(created.OrderCode, GhnCancelReason.Other, null, CancellationToken.None);
        var detail = await client.GetOrderDetailAsync(created.OrderCode, CancellationToken.None);

        Assert.Equal("cancel", detail.Status);
        // Đơn đã hủy không hủy lại được, giống GHN trả `result: false`.
        await Assert.ThrowsAsync<ShippingRejectedException>(
            () => client.CancelOrderAsync(created.OrderCode, GhnCancelReason.Other, null, CancellationToken.None));
    }

    [Fact]
    // Sau khi hủy, tạo lại với cùng mã vẫn trả đơn cũ. Đó là lý do plan đổi `client_order_code` sau mỗi lần hủy.
    public async Task CreateOrder_AfterCancel_SameCodeStillReturnsCancelledOrder()
    {
        var client = new FakeGhnClient();
        var created = await client.CreateOrderAsync(OrderRequest("ORD-1"), CancellationToken.None);
        await client.CancelOrderAsync(created.OrderCode, GhnCancelReason.Other, null, CancellationToken.None);

        var again = await client.CreateOrderAsync(OrderRequest("ORD-1"), CancellationToken.None);
        var renamed = await client.CreateOrderAsync(OrderRequest("ORD-1-2"), CancellationToken.None);

        Assert.Equal(created.OrderCode, again.OrderCode);
        Assert.NotEqual(created.OrderCode, renamed.OrderCode);
    }

    [Fact]
    public async Task UnknownOrderCode_IsRejected_ForDetailAndCancel()
    {
        var client = new FakeGhnClient();

        await Assert.ThrowsAsync<ShippingRejectedException>(() => client.GetOrderDetailAsync("KHONGCO", CancellationToken.None));
        await Assert.ThrowsAsync<ShippingRejectedException>(
            () => client.CancelOrderAsync("KHONGCO", GhnCancelReason.Other, null, CancellationToken.None));
    }

    [Fact]
    // Ngày giao cố định là 15/10/2026 theo giờ Việt Nam, để test của service đoán được kết quả.
    public async Task GetLeadTime_ReturnsFixedPredictableDate()
    {
        var client = new FakeGhnClient();

        var leadTime = await client.GetLeadTimeAsync(new GhnLeadTimeRequest(1000001, 1003646, 2, 1, 1, 1, 1), CancellationToken.None);

        Assert.Equal(FakeGhnClient.FixedLeadTimeUnixSeconds, leadTime.Leadtime);
        Assert.Equal(new DateOnly(2026, 10, 15), GhnLeadTimeDate.ToVietnamDate(leadTime.Leadtime!.Value));
    }

    [Theory]
    [InlineData("GetProvinces")]
    [InlineData("GetWards")]
    [InlineData("CalculateFee")]
    [InlineData("GetLeadTime")]
    [InlineData("CreateOrder")]
    [InlineData("PreviewOrder")]
    [InlineData("CancelOrder")]
    [InlineData("GetOrderDetail")]
    // Tên hàm là chuỗi vì kiểu `FakeGhnOperation` là `internal`, còn phương thức test là `public`.
    public async Task FailureMode_Unavailable_FailsOnlyThatOperation(string failingName)
    {
        var failing = Enum.Parse<FakeGhnOperation>(failingName);
        var client = new FakeGhnClient();
        var created = await client.CreateOrderAsync(OrderRequest("ORD-1"), CancellationToken.None);
        client.SetFailure(failing, FakeGhnFailure.Unavailable);

        foreach (var operation in Enum.GetValues<FakeGhnOperation>())
        {
            var call = Call(client, operation, created.OrderCode);
            if (operation == failing)
            {
                await Assert.ThrowsAsync<ShippingUnavailableException>(call);
            }
            else if (operation != FakeGhnOperation.CancelOrder)
            {
                // Hủy bị bỏ qua ở nhánh thành công vì nó đổi trạng thái đơn dùng chung cho các lần lặp sau.
                await call();
            }
        }
    }

    [Fact]
    public async Task FailureMode_Rejected_ThrowsRejected_AndClearRestoresNormalBehavior()
    {
        var client = new FakeGhnClient();
        client.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Rejected);

        await Assert.ThrowsAsync<ShippingRejectedException>(
            () => client.CalculateFeeAsync(new GhnFeeRequest(1, 1, 1, 1, 2, 0, 0, 1000001, 1003646), CancellationToken.None));

        client.ClearFailures();
        var fee = await client.CalculateFeeAsync(new GhnFeeRequest(1, 1, 1, 1, 2, 0, 0, 1000001, 1003646), CancellationToken.None);
        Assert.Equal(22000, fee.Total);
    }

    [Fact]
    // Mô phỏng GHN đã tạo đơn nhưng phản hồi mất giữa đường. Bấm tạo lại với cùng mã phải trả đúng đơn đó, không tạo đơn thứ hai.
    public async Task FailureMode_UnavailableAfterProcessing_CreatesOrderButThrows_AndRetryReturnsSameOrder()
    {
        var client = new FakeGhnClient();
        client.SetFailure(FakeGhnOperation.CreateOrder, FakeGhnFailure.UnavailableAfterProcessing);

        await Assert.ThrowsAsync<ShippingUnavailableException>(
            () => client.CreateOrderAsync(OrderRequest("ORD-1"), CancellationToken.None));
        client.ClearFailures();
        var retried = await client.CreateOrderAsync(OrderRequest("ORD-1"), CancellationToken.None);
        var detail = await client.GetOrderDetailAsync(retried.OrderCode, CancellationToken.None);

        Assert.Equal("ORD-1", detail.ClientOrderCode);
        // Chỉ có đúng một đơn được tạo: đơn tiếp theo với mã khác phải có số thứ tự kế tiếp.
        var next = await client.CreateOrderAsync(OrderRequest("ORD-2"), CancellationToken.None);
        Assert.NotEqual(retried.OrderCode, next.OrderCode);
        Assert.Equal(
            int.Parse(retried.OrderCode["FAKE".Length..]) + 1,
            int.Parse(next.OrderCode["FAKE".Length..]));
    }

    // Đơn có địa chỉ lấy hàng hai cấp giống request mà `ShippingService` dựng: đủ năm trường và cờ địa chỉ mới.
    private static GhnCreateOrderRequest OrderRequestWithPickup(
        string clientOrderCode,
        string provinceName = "Hà Nội",
        string wardName = "Phường Hoàn Kiếm",
        bool? isNewFromAddress = true) => OrderRequest(clientOrderCode) with
        {
            FromName = "Kho Pickory",
            FromPhone = "0901234567",
            FromAddress = "12 Tràng Tiền",
            FromProvinceName = provinceName,
            FromWardName = wardName,
            IsNewFromAddress = isNewFromAddress,
        };

    [Fact]
    // Đơn có phường lấy hàng đúng danh mục thì GHN nhận và lưu địa chỉ đó, giống kết quả sandbox ngày 2026-10-19.
    public async Task CreateOrder_PickupAddressInCatalogWithNewAddressFlag_CreatesOrder()
    {
        var client = new FakeGhnClient();

        var created = await client.CreateOrderAsync(OrderRequestWithPickup("ORD-FROM-OK"), CancellationToken.None);

        Assert.Equal("FAKE000001", created.OrderCode);
        Assert.Equal(1, client.CreatedOrderCount);
    }

    [Fact]
    // Không gửi địa chỉ lấy hàng thì GHN dùng địa chỉ của shop và vẫn nhận đơn (mốc đã biết của sandbox).
    public async Task CreateOrder_WithoutPickupAddress_CreatesOrder()
    {
        var client = new FakeGhnClient();

        await client.CreateOrderAsync(OrderRequest("ORD-NO-FROM"), CancellationToken.None);

        Assert.Equal(1, client.CreatedOrderCount);
    }

    [Theory]
    [InlineData("Hà Nội", "Phường Không Có")]
    [InlineData("Hà Nội", "Phường Bến Nghé")]
    [InlineData("Tỉnh Không Có", "Phường Hoàn Kiếm")]
    // Ba ca của sandbox ngày 2026-10-19 (ADR 0003, mục "Bổ sung SHIP-11"): phường lạ, phường của tỉnh khác, tỉnh lạ.
    // Bản giả đi qua cùng bước dịch với adapter thật, nên test thấy câu tiếng Việt.
    public async Task CreateOrder_PickupWardOrProvinceNotInCatalog_IsRejectedWithTranslatedMessage(
        string provinceName, string wardName)
    {
        var client = new FakeGhnClient();

        var error = await Assert.ThrowsAsync<ShippingRejectedException>(() => client.CreateOrderAsync(
            OrderRequestWithPickup("ORD-FROM-BAD", provinceName, wardName), CancellationToken.None));

        Assert.Equal(ShippingRejectionKind.PickupPlaceNotInCatalog, error.Kind);
        Assert.Contains("Cập nhật lại ở trang cấu hình vận chuyển.", error.Message);
        Assert.Equal(0, client.CreatedOrderCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    // Luật riêng của bản giả: sandbox nhận đơn thiếu cờ và bỏ qua phường, còn bản giả từ chối để lỗi cấu hình lộ ra.
    public async Task CreateOrder_PickupAddressWithoutNewAddressFlag_IsRejectedByFakeOnly(bool? isNewFromAddress)
    {
        var client = new FakeGhnClient();

        var error = await Assert.ThrowsAsync<ShippingRejectedException>(() => client.CreateOrderAsync(
            OrderRequestWithPickup("ORD-FROM-FLAG", isNewFromAddress: isNewFromAddress), CancellationToken.None));

        Assert.Contains("is_new_from_address", error.Message);
        Assert.Equal(0, client.CreatedOrderCount);
    }

    [Fact]
    // Quy ước cho test của `commerce`: dòng địa chỉ nhận chứa `RejectedAddressMarker` thì GHN giả báo `To address conflict`.
    public async Task CreateOrder_ToAddressWithRejectedMarker_IsRejectedAsAddressMismatchAndCreatesNothing()
    {
        var client = new FakeGhnClient();
        var request = OrderRequest("ORD-TO-BAD") with { ToAddress = $"1 Nguyen Hue {FakeGhnClient.RejectedAddressMarker}" };

        var error = await Assert.ThrowsAsync<ShippingRejectedException>(() => client.CreateOrderAsync(request, CancellationToken.None));

        Assert.Equal(ShippingRejectionKind.ToAddressMismatch, error.Kind);
        Assert.Contains("tên đường không khớp với phường/xã", error.Message);
        Assert.Equal(0, client.CreatedOrderCount);
    }

    [Fact]
    public async Task PreviewOrder_ValidAddress_ReturnsFeeAndCreatesNothing()
    {
        var client = new FakeGhnClient();

        var preview = await client.PreviewOrderAsync(OrderRequestWithPickup("ORD-PREVIEW-OK"), CancellationToken.None);

        Assert.True(preview.TotalFee > 0);
        // API xem trước của GHN không tạo đơn. Bản giả cũng không tạo đơn và không tăng bộ đếm.
        Assert.Equal(0, client.CreatedOrderCount);
    }

    [Fact]
    public async Task PreviewOrder_ToAddressWithRejectedMarker_IsRejectedAsAddressMismatch()
    {
        var client = new FakeGhnClient();
        var request = OrderRequest("ORD-PREVIEW-TO") with { ToAddress = $"{FakeGhnClient.RejectedAddressMarker} 1 Nguyen Hue" };

        var error = await Assert.ThrowsAsync<ShippingRejectedException>(() => client.PreviewOrderAsync(request, CancellationToken.None));

        Assert.Equal(ShippingRejectionKind.ToAddressMismatch, error.Kind);
        Assert.Equal(0, client.CreatedOrderCount);
    }

    [Fact]
    public async Task PreviewOrder_PickupWardNotInCatalog_IsRejectedAsPickupPlaceNotInCatalog()
    {
        var client = new FakeGhnClient();

        var error = await Assert.ThrowsAsync<ShippingRejectedException>(() => client.PreviewOrderAsync(
            OrderRequestWithPickup("ORD-PREVIEW-FROM", wardName: "Phường Không Có"), CancellationToken.None));

        Assert.Equal(ShippingRejectionKind.PickupPlaceNotInCatalog, error.Kind);
    }

    private static Func<Task> Call(FakeGhnClient client, FakeGhnOperation operation, string orderCode) => operation switch
    {
        FakeGhnOperation.GetProvinces => () => client.GetProvincesAsync(CancellationToken.None),
        FakeGhnOperation.GetWards => () => client.GetWardsAsync("khong-co", 0, 10, CancellationToken.None),
        FakeGhnOperation.CalculateFee => () => client.CalculateFeeAsync(new GhnFeeRequest(1, 1, 1, 1, 2, 0, 0, 1000001, 1003646), CancellationToken.None),
        FakeGhnOperation.GetLeadTime => () => client.GetLeadTimeAsync(new GhnLeadTimeRequest(1000001, 1003646, 2, 1, 1, 1, 1), CancellationToken.None),
        FakeGhnOperation.CreateOrder => () => client.CreateOrderAsync(OrderRequest("ORD-LOOP"), CancellationToken.None),
        FakeGhnOperation.PreviewOrder => () => client.PreviewOrderAsync(OrderRequest("ORD-LOOP-PREVIEW"), CancellationToken.None),
        FakeGhnOperation.CancelOrder => () => client.CancelOrderAsync(orderCode, GhnCancelReason.Other, null, CancellationToken.None),
        _ => () => client.GetOrderDetailAsync(orderCode, CancellationToken.None),
    };
}
