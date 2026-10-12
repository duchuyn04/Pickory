using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-07: `POST /api/checkout/previews` đọc giỏ của người gọi, báo phí qua shipping và không ghi DB.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CheckoutPreviewTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string PreviewPath = "/api/checkout/previews";

    // Mã và tên lấy theo danh mục của `QuoteTestSetup.SeedCatalogAsync`.
    private const string ActiveProvinceCode = "1000001";
    private const string ActiveWardCode = "1003646";
    private const string ActiveProvinceName = "Hồ Chí Minh";
    private const string ActiveWardName = "Phường Bến Nghé";
    private const string DisabledWardCode = "1003649";
    private const string WardOfOtherProvinceCode = "1004001";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly CartTestKit _kit;

    public CheckoutPreviewTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _kit = new CartTestKit(factory, database);
    }

    private FakeGhnClient Fake => _factory.GhnClient;

    private RecordingGhnClient Recorder => _factory.Recorder;

    // Bản bọc GHN là singleton của factory dùng chung cho mọi test của lớp. Số lời gọi tính từ lúc test bắt đầu.
    private int _callCountAtStart;

    private int ShippingCallCount => Recorder.TotalCallCount - _callCountAtStart;

    public async Task InitializeAsync()
    {
        await _kit.InitializeAsync();
        await QuoteTestSetup.ResetAsync(_factory);
        await QuoteTestSetup.SeedCatalogAsync(_factory);
        await QuoteTestSetup.SeedSettingsAsync(_factory, defaultServiceTypeId: 2);

        // Bộ đếm nằm trong singleton của factory dùng chung cho mọi test của lớp. Xóa trước mỗi test để các test không ảnh hưởng nhau.
        _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
        Fake.ClearFailures();
        Recorder.FeeRequests.Clear();
        _callCountAtStart = Recorder.TotalCallCount;
    }

    public async Task DisposeAsync()
    {
        Fake.ClearFailures();
        await _kit.CleanupAsync();
        await QuoteTestSetup.ResetAsync(_factory);
    }

    private static object BodyFor(
        string paymentMethod = "cod",
        string provinceCode = ActiveProvinceCode,
        string wardCode = ActiveWardCode,
        string line = "12 Phố Test") =>
        new { address = new { provinceCode, wardCode, line }, paymentMethod };

    private Task<HttpResponseMessage> PreviewAsync(
        string anonymousId, object? body = null, string? sessionToken = null) =>
        _kit.SendAsync(HttpMethod.Post, PreviewPath, anonymousId, body ?? BodyFor(), sessionToken);

    // Thêm một sản phẩm một SKU vào giỏ vãng lai bằng route giỏ thật và trả id SKU.
    private async Task<string> AddToCartAsync(string anonymousId, int price, int quantity = 1, int stock = 10)
    {
        var product = await _kit.AddProductAsync(price: price, stock: stock);
        var skuId = product.SkuIds[0];
        var response = await _kit.AddAsync(anonymousId, skuId, quantity);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return skuId;
    }

    // Đặt dòng cấu hình commerce có ngưỡng và hạn mức do test chọn. `CleanupAsync` của kit xóa dòng này.
    private async Task SetSettingsAsync(int freeShippingThreshold, int codMaxAmount)
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        commerce.Settings.Add(new CommerceSettings
        {
            Id = "default",
            FreeShippingThreshold = freeShippingThreshold,
            CodMaxAmount = codMaxAmount,
            MaxQuantityPerSku = 5,
            MaxCartLines = 20,
            UpdatedAt = DateTime.UtcNow,
        });
        await commerce.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response) =>
        await CartTestKit.ReadCartAsync(response);

    private static async Task<JsonElement> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code) =>
        await CartTestKit.AssertErrorAsync(response, status, code);

    private static string[] DetailKeys(JsonElement error) =>
        error.GetProperty("details").EnumerateArray().Select(item => item.GetProperty("key").GetString()!).ToArray();

    // Phí của `FakeGhnClient`: 20.000 đ cộng 2.000 đ cho mỗi 500 g làm tròn lên, cộng giá trị khai giá chia 200.
    private static int FakeFee(int weightGrams, int insuredValue) =>
        20_000 + (2_000 * ((weightGrams + 499) / 500)) + (insuredValue / 200);

    // ----- Tiêu chí 1: phí lấy từ báo phí, kiện dựng từ cân nặng và kích thước của SKU -----

    [Fact]
    public async Task Preview_BuildsParcelFromSkuMeasuresAndQuantities_AndReturnsCarrierFee()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000, quantity: 2);
        var skuB = await AddToCartAsync(anonymousId, price: 100_000, quantity: 1);
        // SKU A giữ số đo mặc định của dữ liệu test: 300 g, 20 x 10 x 2 cm.
        await _kit.UpdateSkuAsync(skuB, sku =>
        {
            sku.WeightGrams = 1200;
            sku.LengthCm = 35;
            sku.WidthCm = 25;
            sku.HeightCm = 8;
        });

        var response = await PreviewAsync(anonymousId);

        var data = await ReadDataAsync(response);
        // Cân nặng 2 x 300 + 1200, dài và rộng lấy lớn nhất, cao 2 x 2 + 8. Giá trị khai giá bằng tạm tính 300.000 đ.
        var request = Assert.Single(Recorder.FeeRequests);
        Assert.Equal(1800, request.Weight);
        Assert.Equal(35, request.Length);
        Assert.Equal(25, request.Width);
        Assert.Equal(12, request.Height);
        Assert.Equal(300_000, request.InsuranceValue);
        Assert.Equal(0, request.CodValue);

        var expectedFee = FakeFee(1800, 300_000);
        Assert.Equal(300_000, data.GetProperty("subtotal").GetInt32());
        Assert.Equal(expectedFee, data.GetProperty("shippingFee").GetInt32());
        Assert.Equal(0, data.GetProperty("shippingDiscount").GetInt32());
        Assert.Equal(expectedFee, data.GetProperty("customerShippingFee").GetInt32());
        Assert.Equal(300_000 + expectedFee, data.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Preview_ReturnsExpectedDeliveryDateAsIsoDate()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId));

        Assert.Equal("2026-10-15", data.GetProperty("expectedDeliveryDate").GetString());
    }

    [Fact]
    public async Task Preview_CarrierGivesNoDeliveryDate_ReturnsNullDate()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        Fake.SetFailure(FakeGhnOperation.GetLeadTime, FakeGhnFailure.Unavailable);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId));

        Assert.Equal(JsonValueKind.Null, data.GetProperty("expectedDeliveryDate").ValueKind);
        Assert.True(data.GetProperty("shippingFee").GetInt32() > 0);
    }

    [Fact]
    public async Task Preview_ReturnsThresholdAndCodLimitFromSettings()
    {
        await SetSettingsAsync(freeShippingThreshold: 750_000, codMaxAmount: 5_000_000);
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId));

        Assert.Equal(750_000, data.GetProperty("freeShippingThreshold").GetInt32());
        Assert.Equal(5_000_000, data.GetProperty("codMaxAmount").GetInt32());
        Assert.True(data.GetProperty("codAllowed").GetBoolean());
    }

    [Fact]
    public async Task Preview_WithoutSettingsRow_UsesDefaultThresholdAndCodLimit()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId));

        Assert.Equal(500_000, data.GetProperty("freeShippingThreshold").GetInt32());
        Assert.Equal(20_000_000, data.GetProperty("codMaxAmount").GetInt32());
    }

    // ----- Tiêu chí 2: ngưỡng miễn phí vận chuyển -----

    [Fact]
    public async Task Preview_SubtotalOneDongBelowThreshold_CustomerPaysShippingFee()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 499_999);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId));

        var fee = data.GetProperty("shippingFee").GetInt32();
        Assert.True(fee > 0);
        Assert.Equal(0, data.GetProperty("shippingDiscount").GetInt32());
        Assert.Equal(fee, data.GetProperty("customerShippingFee").GetInt32());
        Assert.Equal(499_999 + fee, data.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Preview_SubtotalExactlyAtThreshold_CustomerShippingFeeIsZero()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 500_000);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId));

        var fee = data.GetProperty("shippingFee").GetInt32();
        Assert.True(fee > 0);
        Assert.Equal(fee, data.GetProperty("shippingDiscount").GetInt32());
        Assert.Equal(0, data.GetProperty("customerShippingFee").GetInt32());
        Assert.Equal(500_000, data.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Preview_ThresholdComesFromSettingsRow()
    {
        await SetSettingsAsync(freeShippingThreshold: 100_000, codMaxAmount: 20_000_000);
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId));

        Assert.Equal(0, data.GetProperty("customerShippingFee").GetInt32());
        Assert.Equal(100_000, data.GetProperty("total").GetInt32());
    }

    // ----- Tiêu chí 3: hạn mức COD -----

    [Fact]
    public async Task Preview_CodTotalExactlyAtLimit_ReturnsOkAndCodAllowed()
    {
        // Tạm tính 1.000.000 đ vượt ngưỡng 500.000 đ nên tổng tiền bằng tạm tính.
        await SetSettingsAsync(freeShippingThreshold: 500_000, codMaxAmount: 1_000_000);
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 1_000_000);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId, BodyFor("cod")));

        Assert.Equal(1_000_000, data.GetProperty("total").GetInt32());
        Assert.True(data.GetProperty("codAllowed").GetBoolean());
    }

    [Fact]
    public async Task Preview_CodTotalOneDongAboveLimit_Returns422CodNotAllowed()
    {
        await SetSettingsAsync(freeShippingThreshold: 500_000, codMaxAmount: 999_999);
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 1_000_000);

        var response = await PreviewAsync(anonymousId, BodyFor("cod"));

        await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "COD_NOT_ALLOWED");
    }

    [Fact]
    public async Task Preview_SepayTotalAboveCodLimit_ReturnsOkWithCodAllowedFalse()
    {
        await SetSettingsAsync(freeShippingThreshold: 500_000, codMaxAmount: 999_999);
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 1_000_000);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId, BodyFor("sepay")));

        Assert.False(data.GetProperty("codAllowed").GetBoolean());
        Assert.Equal(999_999, data.GetProperty("codMaxAmount").GetInt32());
        Assert.Equal(1_000_000, data.GetProperty("total").GetInt32());
    }

    // ----- Tiêu chí 4: lỗi shipping đi ra nguyên mã, không có phí ước đoán -----

    [Fact]
    public async Task Preview_CarrierUnavailable_Returns503WithoutAnyFee()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        Fake.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Unavailable);

        var response = await PreviewAsync(anonymousId);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(text);
        Assert.Equal("SHIPPING_UNAVAILABLE", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(document.RootElement.TryGetProperty("data", out _));
        Assert.DoesNotContain("shippingFee", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_CarrierRejects_Returns422WithCarrierCode()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        Fake.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Rejected);

        var response = await PreviewAsync(anonymousId);

        await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "SHIPPING_REJECTED");
    }

    [Fact]
    public async Task Preview_ShippingNotConfigured_Returns503WithItsOwnCode()
    {
        await QuoteTestSetup.ResetAsync(_factory);
        await QuoteTestSetup.SeedCatalogAsync(_factory);
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);

        var response = await PreviewAsync(anonymousId);

        await AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, "SHIPPING_NOT_CONFIGURED");
    }

    // ----- Tiêu chí 5: mã địa chỉ phải có trong danh mục -----

    [Theory]
    [InlineData("9999999", ActiveWardCode)]
    [InlineData(ActiveProvinceCode, "9999999")]
    [InlineData(ActiveProvinceCode, WardOfOtherProvinceCode)]
    [InlineData(ActiveProvinceCode, DisabledWardCode)]
    public async Task Preview_AddressNotInCatalog_Returns400OnAddressField(string provinceCode, string wardCode)
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);

        var response = await PreviewAsync(anonymousId, BodyFor(provinceCode: provinceCode, wardCode: wardCode));

        var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(["address"], DetailKeys(error));
        Assert.Empty(Recorder.FeeRequests);
    }

    [Fact]
    public async Task Preview_ReturnsNamesFromCatalogAndTrimmedLine()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);

        var response = await PreviewAsync(anonymousId, BodyFor(line: "   12 Phố Test   "));

        var address = (await ReadDataAsync(response)).GetProperty("address");
        Assert.Equal(ActiveProvinceCode, address.GetProperty("provinceCode").GetString());
        Assert.Equal(ActiveProvinceName, address.GetProperty("provinceName").GetString());
        Assert.Equal(ActiveWardCode, address.GetProperty("wardCode").GetString());
        Assert.Equal(ActiveWardName, address.GetProperty("wardName").GetString());
        Assert.Equal("12 Phố Test", address.GetProperty("line").GetString());
    }

    [Fact]
    public async Task Preview_FrontendSuppliedNames_AreRejectedAsUnknownFields()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var body = """
            {"address":{"provinceCode":"1000001","provinceName":"Tên giả","wardCode":"1003646","line":"12 Phố Test"},"paymentMethod":"cod"}
            """;

        var response = await PreviewAsync(anonymousId, body);

        var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(["address.provinceName"], DetailKeys(error));
    }

    // ----- Tiêu chí 6: giới hạn tần suất -----

    [Fact]
    public async Task Preview_TwentyFirstRequestWithinWindow_Returns429()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);

        for (var attempt = 1; attempt <= CheckoutPreviewRateLimiter.MaxRequestsPerWindow; attempt++)
        {
            var allowed = await PreviewAsync(anonymousId);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var limited = await PreviewAsync(anonymousId);

        await AssertErrorAsync(limited, HttpStatusCode.TooManyRequests, "TOO_MANY_REQUESTS");
    }

    [Fact]
    public async Task Preview_RateLimitIsCheckedBeforeBodyValidation()
    {
        var anonymousId = _kit.NewAnonymousId();
        for (var attempt = 1; attempt <= CheckoutPreviewRateLimiter.MaxRequestsPerWindow; attempt++)
        {
            await PreviewAsync(anonymousId, """{"khong":"hop le"}""");
        }

        var limited = await PreviewAsync(anonymousId, """{"khong":"hop le"}""");

        await AssertErrorAsync(limited, HttpStatusCode.TooManyRequests, "TOO_MANY_REQUESTS");
    }

    [Fact]
    public async Task Preview_VisitorLimitIsPerIp_SoNewAnonymousIdDoesNotResetIt()
    {
        var firstAnonymousId = _kit.NewAnonymousId();
        for (var attempt = 1; attempt <= CheckoutPreviewRateLimiter.MaxRequestsPerWindow; attempt++)
        {
            await PreviewAsync(firstAnonymousId, """{"khong":"hop le"}""");
        }

        var limited = await PreviewAsync(_kit.NewAnonymousId(), """{"khong":"hop le"}""");

        await AssertErrorAsync(limited, HttpStatusCode.TooManyRequests, "TOO_MANY_REQUESTS");
    }

    [Fact]
    public async Task Preview_SignedInUsersHaveSeparateCountersFromEachOtherAndFromTheIp()
    {
        var (_, firstToken) = await _kit.CreateCustomerAsync();
        var (_, secondToken) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        for (var attempt = 1; attempt <= CheckoutPreviewRateLimiter.MaxRequestsPerWindow; attempt++)
        {
            await PreviewAsync(anonymousId, """{"khong":"hop le"}""", firstToken);
        }

        var firstUserLimited = await PreviewAsync(anonymousId, """{"khong":"hop le"}""", firstToken);
        var secondUserAllowed = await PreviewAsync(anonymousId, """{"khong":"hop le"}""", secondToken);
        var visitorAllowed = await PreviewAsync(anonymousId, """{"khong":"hop le"}""");

        await AssertErrorAsync(firstUserLimited, HttpStatusCode.TooManyRequests, "TOO_MANY_REQUESTS");
        await AssertErrorAsync(secondUserAllowed, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertErrorAsync(visitorAllowed, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    // ----- Tiêu chí 7: dòng không mua được -----

    public enum UnavailableCause
    {
        SkuDisabled,
        ProductDiscontinued,
        OutOfStock,
        QuantityAboveStock,
        ProductHidden,
        ProductDeleted,
    }

    [Theory]
    [InlineData(UnavailableCause.SkuDisabled)]
    [InlineData(UnavailableCause.ProductDiscontinued)]
    [InlineData(UnavailableCause.OutOfStock)]
    [InlineData(UnavailableCause.QuantityAboveStock)]
    [InlineData(UnavailableCause.ProductHidden)]
    [InlineData(UnavailableCause.ProductDeleted)]
    public async Task Preview_CartHasLineThatCannotBePurchased_Returns422WithoutCallingShipping(UnavailableCause cause)
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = await _kit.AddProductAsync(price: 100_000, stock: 10);
        var skuId = product.SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId, quantity: 2);
        // Một dòng còn mua được đứng cạnh. Dòng hợp lệ không che được dòng sai.
        await AddToCartAsync(anonymousId, price: 100_000);

        switch (cause)
        {
            case UnavailableCause.SkuDisabled:
                await _kit.UpdateSkuAsync(skuId, sku => sku.Enabled = false);
                break;
            case UnavailableCause.ProductDiscontinued:
                await _kit.SetProductStatusAsync(product.Id, ProductStatus.Discontinued);
                break;
            case UnavailableCause.OutOfStock:
                await _kit.UpdateSkuAsync(skuId, sku => sku.Stock = 0);
                break;
            case UnavailableCause.QuantityAboveStock:
                await _kit.UpdateSkuAsync(skuId, sku => sku.Stock = 1);
                break;
            case UnavailableCause.ProductHidden:
                await _kit.SetProductStatusAsync(product.Id, ProductStatus.Hidden);
                break;
            case UnavailableCause.ProductDeleted:
                await _kit.DeleteProductAsync(product.Id);
                break;
        }

        var response = await PreviewAsync(anonymousId);

        await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "SKU_UNAVAILABLE");
        Assert.Equal(0, ShippingCallCount);
    }

    // ----- Quyết định 4.6: SKU bán được nhưng thiếu cân nặng hoặc kích thước -----

    [Theory]
    [InlineData("weight")]
    [InlineData("length")]
    [InlineData("width")]
    [InlineData("height")]
    public async Task Preview_SkuMissingMeasure_Returns422LogsErrorWithSkuIdAndDoesNotCallShipping(string missing)
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000);
        await _kit.UpdateSkuAsync(skuId, sku =>
        {
            if (missing == "weight") sku.WeightGrams = null;
            if (missing == "length") sku.LengthCm = null;
            if (missing == "width") sku.WidthCm = null;
            if (missing == "height") sku.HeightCm = null;
        });

        var response = await PreviewAsync(anonymousId);

        await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "SKU_UNAVAILABLE");
        Assert.Equal(0, ShippingCallCount);
        Assert.Contains(_factory.Logs.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains(skuId, StringComparison.Ordinal));
    }

    // ----- Quyết định 4.4: giỏ rỗng -----

    [Fact]
    public async Task Preview_VisitorWithoutCart_Returns400EmptyCartAndCreatesNoCart()
    {
        var anonymousId = _kit.NewAnonymousId();

        var response = await PreviewAsync(anonymousId);

        var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Contains("Giỏ hàng đang trống.", error.GetProperty("message").GetString());
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
        Assert.Equal(0, ShippingCallCount);
    }

    [Fact]
    public async Task Preview_CartExistsButHasNoLines_Returns400EmptyCart()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000);
        await _kit.RemoveAsync(anonymousId, skuId);

        var response = await PreviewAsync(anonymousId);

        var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Contains("Giỏ hàng đang trống.", error.GetProperty("message").GetString());
    }

    // ----- Thứ tự kiểm của quyết định 4 -----

    [Fact]
    public async Task Preview_InvalidBodyAndEmptyCart_ReportsBodyErrorFirst()
    {
        var anonymousId = _kit.NewAnonymousId();

        var response = await PreviewAsync(anonymousId, """{"address":{"provinceCode":"1000001","wardCode":"1003646","line":"x"}}""");

        var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(["paymentMethod"], DetailKeys(error));
    }

    [Fact]
    public async Task Preview_UnknownAddressAndEmptyCart_ReportsAddressErrorFirst()
    {
        var anonymousId = _kit.NewAnonymousId();

        var response = await PreviewAsync(anonymousId, BodyFor(provinceCode: "9999999"));

        var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(["address"], DetailKeys(error));
    }

    [Fact]
    public async Task Preview_UnknownAddressAndUnavailableLine_ReportsAddressErrorFirst()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000);
        await _kit.UpdateSkuAsync(skuId, sku => sku.Enabled = false);

        var response = await PreviewAsync(anonymousId, BodyFor(provinceCode: "9999999"));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Preview_UnavailableLineAndMissingMeasure_ReportsUnavailableWithoutErrorLog()
    {
        var anonymousId = _kit.NewAnonymousId();
        var unavailableSku = await AddToCartAsync(anonymousId, price: 100_000);
        var noMeasureSku = await AddToCartAsync(anonymousId, price: 100_000);
        await _kit.UpdateSkuAsync(unavailableSku, sku => sku.Enabled = false);
        await _kit.UpdateSkuAsync(noMeasureSku, sku => sku.WeightGrams = null);

        var response = await PreviewAsync(anonymousId);

        await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "SKU_UNAVAILABLE");
        Assert.DoesNotContain(_factory.Logs.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains(noMeasureSku, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_CodAboveLimitAndShippingDown_ReportsShippingErrorBecauseQuoteComesFirst()
    {
        await SetSettingsAsync(freeShippingThreshold: 500_000, codMaxAmount: 999_999);
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 1_000_000);
        Fake.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Unavailable);

        var response = await PreviewAsync(anonymousId, BodyFor("cod"));

        await AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, "SHIPPING_UNAVAILABLE");
    }

    // ----- Chủ giỏ -----

    [Fact]
    public async Task Preview_SignedInCustomer_UsesUserCartAndIgnoresAnonymousCart()
    {
        var (userId, token) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var userSku = (await _kit.AddProductAsync(price: 200_000)).SkuIds[0];
        var anonymousSku = (await _kit.AddProductAsync(price: 300_000)).SkuIds[0];
        await _kit.SeedCartAsync(userId, null, (userSku, 1));
        await _kit.SeedCartAsync(null, anonymousId, (anonymousSku, 1));

        var response = await PreviewAsync(anonymousId, sessionToken: token);

        var data = await ReadDataAsync(response);
        Assert.Equal(200_000, data.GetProperty("subtotal").GetInt32());
    }

    [Fact]
    public async Task Preview_Visitor_UsesAnonymousCartAndIgnoresUserCart()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var userSku = (await _kit.AddProductAsync(price: 200_000)).SkuIds[0];
        var anonymousSku = (await _kit.AddProductAsync(price: 300_000)).SkuIds[0];
        await _kit.SeedCartAsync(userId, null, (userSku, 1));
        await _kit.SeedCartAsync(null, anonymousId, (anonymousSku, 2));

        var response = await PreviewAsync(anonymousId);

        var data = await ReadDataAsync(response);
        Assert.Equal(600_000, data.GetProperty("subtotal").GetInt32());
    }

    [Fact]
    public async Task Preview_VisitorWithCookieOfLinkedUserCart_DoesNotSeeTheUserCart()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var userSku = (await _kit.AddProductAsync(price: 200_000)).SkuIds[0];
        // Giỏ của tài khoản vẫn giữ `AnonymousId` cũ. Khách chưa đăng nhập mang cookie đó không được thấy giỏ này.
        await _kit.InsertCartAsync(userId, anonymousId, userSku, 1);

        var response = await PreviewAsync(anonymousId);

        var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Contains("Giỏ hàng đang trống.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Preview_TwoVisitors_EachSeeOnlyTheirOwnCart()
    {
        var firstId = _kit.NewAnonymousId();
        var secondId = _kit.NewAnonymousId();
        await AddToCartAsync(firstId, price: 100_000);
        await AddToCartAsync(secondId, price: 400_000);

        var first = await ReadDataAsync(await PreviewAsync(firstId));
        var second = await ReadDataAsync(await PreviewAsync(secondId));

        Assert.Equal(100_000, first.GetProperty("subtotal").GetInt32());
        Assert.Equal(400_000, second.GetProperty("subtotal").GetInt32());
    }

    // ----- Giá và quyết định 9: giá lấy từ catalog, preview không ghi DB -----

    [Fact]
    public async Task Preview_UsesCurrentCatalogPrice()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 2);
        await _kit.UpdateSkuAsync(skuId, sku => sku.Price = 120_000);

        var data = await ReadDataAsync(await PreviewAsync(anonymousId));

        Assert.Equal(240_000, data.GetProperty("subtotal").GetInt32());
    }

    [Fact]
    public async Task Preview_DoesNotChangeCartStockOrOrders()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 3);
        var stockBefore = await _kit.StockOfAsync(skuId);
        var movementsBefore = await _kit.MovementCountOfAsync(skuId);
        var cartsBefore = await _kit.CartsOfAnonymousAsync(anonymousId);

        await ReadDataAsync(await PreviewAsync(anonymousId));

        var cartsAfter = await _kit.CartsOfAnonymousAsync(anonymousId);
        var cartBefore = Assert.Single(cartsBefore);
        var cartAfter = Assert.Single(cartsAfter);
        Assert.Equal(cartBefore.UpdatedAt, cartAfter.UpdatedAt);
        Assert.Equal(3, Assert.Single(cartAfter.Items).Quantity);
        Assert.Equal(stockBefore, await _kit.StockOfAsync(skuId));
        Assert.Equal(movementsBefore, await _kit.MovementCountOfAsync(skuId));
        await using var commerce = _kit.Database.CreateCommerceContext();
        Assert.Empty(commerce.Orders);
    }

    // ----- Body sai qua HTTP -----

    [Fact]
    public async Task Preview_BodyWithLineItems_Returns400OnThatField()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var body = """
            {"address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố Test"},"paymentMethod":"cod","items":[{"skuId":"x","quantity":1}]}
            """;

        var response = await PreviewAsync(anonymousId, body);

        var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(["items"], DetailKeys(error));
    }

    [Fact]
    public async Task Preview_LineLongerThan200Characters_Returns400OnLineField()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);

        var response = await PreviewAsync(anonymousId, BodyFor(line: new string('a', 201)));

        var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(["address.line"], DetailKeys(error));
    }
}
