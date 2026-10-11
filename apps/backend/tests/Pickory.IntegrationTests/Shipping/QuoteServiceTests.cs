using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Quotes;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-05 lát 2: `QuoteAsync` kiểm tham số, cấu hình, địa chỉ nhận, bảo hiểm và gói dịch vụ trước khi gọi GHN.
// Dùng DB thật và `FakeGhnClient` bọc trong `RecordingGhnClient`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class QuoteServiceTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private static readonly ShippingAddress ActiveAddress = QuoteTestSetup.ActiveAddress;

    private readonly RecordingGhnClient _ghn = new(new FakeGhnClient());
    private readonly ListLogger<ShippingService> _logger = new();

    public Task InitializeAsync() => QuoteTestSetup.ResetAsync(factory);

    public Task DisposeAsync() => QuoteTestSetup.ResetAsync(factory);

    private Task SeedCatalogAsync() => QuoteTestSetup.SeedCatalogAsync(factory);

    private Task SeedSettingsAsync(int? defaultServiceTypeId) => QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId);

    private Task<Quote> QuoteAsync(ShippingAddress? to = null, int codAmount = 0, int insuredValue = 0) =>
        QuoteTestSetup.QuoteAsync(factory, _ghn, _logger, to, codAmount: codAmount, insuredValue: insuredValue);

    [Fact]
    public async Task Quote_NotConfigured_ThrowsNotConfiguredAndDoesNotCallGhn()
    {
        await SeedCatalogAsync();

        await Assert.ThrowsAsync<ShippingNotConfiguredException>(() => QuoteAsync());

        Assert.Equal(0, _ghn.TotalCallCount);
        // Thiếu cấu hình là việc của admin, nên phải ghi log mức lỗi để người vận hành thấy.
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData("9999", "1000001")]
    [InlineData("1003649", "1000001")]
    [InlineData("1004001", "1000001")]
    // Phường không có, đã tắt, hoặc thuộc tỉnh khác với tỉnh khách gửi: GHN không được gọi.
    public async Task Quote_AddressNotInCatalog_ThrowsRejectedAndDoesNotCallGhn(string wardCode, string provinceCode)
    {
        await SeedCatalogAsync();
        await SeedSettingsAsync(defaultServiceTypeId: null);
        var address = ActiveAddress with { WardCode = wardCode, ProvinceCode = provinceCode };

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => QuoteAsync(address));

        Assert.Equal("SHIPPING_REJECTED", exception.Code);
        Assert.Contains("Địa chỉ giao hàng không còn trong danh mục.", exception.Message);
        Assert.Equal(0, _ghn.TotalCallCount);
    }

    [Fact]
    // Danh mục có phường mã không phải số là dữ liệu hỏng. Module không gọi GHN và ghi log mức lỗi để người vận hành sửa.
    public async Task Quote_WardCodeIsNotNumeric_ThrowsRejectedAndDoesNotCallGhn()
    {
        await SeedCatalogAsync();
        await SeedSettingsAsync(defaultServiceTypeId: null);
        await QuoteTestSetup.SeedActiveWardAsync(factory, "ABC", "1000001");
        var address = ActiveAddress with { WardCode = "ABC" };

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => QuoteAsync(address));

        Assert.Equal("SHIPPING_REJECTED", exception.Code);
        Assert.Contains("Địa chỉ giao hàng có mã không hợp lệ.", exception.Message);
        Assert.Equal(0, _ghn.TotalCallCount);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    // Phường có trong danh mục của shop nhưng GHN không biết: bản giả từ chối như GHN và module trả `SHIPPING_REJECTED`.
    public async Task Quote_WardUnknownToGhn_ThrowsRejectedFromCarrier()
    {
        await SeedCatalogAsync();
        await SeedSettingsAsync(defaultServiceTypeId: null);
        await QuoteTestSetup.SeedActiveWardAsync(factory, "5555", "1000001");
        var address = ActiveAddress with { WardCode = "5555" };

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => QuoteAsync(address));

        Assert.Equal("SHIPPING_REJECTED", exception.Code);
        Assert.Contains("mã phường mới không tồn tại: 5555", exception.Message);
        // Lời gọi đã tới GHN: một lần báo phí, và lời gọi ngày giao bị hủy cùng lúc.
        Assert.Single(_ghn.FeeRequests);
    }

    [Fact]
    public async Task Quote_CodAmountAboveGhnLimit_ThrowsRejectedAndDoesNotCallGhn()
    {
        await SeedCatalogAsync();
        await SeedSettingsAsync(defaultServiceTypeId: null);

        await Assert.ThrowsAsync<ShippingRejectedException>(() => QuoteAsync(codAmount: 50_000_001));

        Assert.Equal(0, _ghn.TotalCallCount);
    }

    [Fact]
    public async Task Quote_CodAmountEqualToGhnLimit_CallsGhn()
    {
        await SeedCatalogAsync();
        await SeedSettingsAsync(defaultServiceTypeId: null);

        await QuoteAsync(codAmount: 50_000_000);

        var request = Assert.Single(_ghn.FeeRequests);
        Assert.Equal(50_000_000, request.CodValue);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public async Task Quote_NegativeAmount_ThrowsArgumentOutOfRange(int codAmount, int insuredValue)
    {
        await SeedCatalogAsync();
        await SeedSettingsAsync(defaultServiceTypeId: null);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => QuoteAsync(codAmount: codAmount, insuredValue: insuredValue));

        Assert.Equal(0, _ghn.TotalCallCount);
    }

    [Fact]
    // Phí của `FakeGhnClient`: 20.000 đ, cộng 2.000 đ cho mỗi 500 g bắt đầu (1.500 g là 3 khối), cộng bảo hiểm 0,5%.
    // Với 1.000.000 đ giá trị khai, bảo hiểm là 5.000 đ nên tổng là 31.000 đ.
    public async Task Quote_Success_ReturnsFeeAndConfiguredServiceType()
    {
        await SeedCatalogAsync();
        await SeedSettingsAsync(defaultServiceTypeId: 5);

        var quote = await QuoteAsync(insuredValue: 1_000_000);

        Assert.Equal(31_000, quote.Fee);
        Assert.Equal(5, quote.ServiceTypeId);
        var request = Assert.Single(_ghn.FeeRequests);
        Assert.Equal(5, request.ServiceTypeId);
        // Mã đã lưu dạng chuỗi được đổi sang số trước khi gửi, và cờ địa chỉ 2 cấp luôn bật.
        Assert.Equal(1000001L, request.ToProvinceIdV2);
        Assert.Equal(1003646L, request.ToWardIdV2);
        Assert.True(request.IsNewToAddress);
        Assert.Equal(1500, request.Weight);
        Assert.Equal(30, request.Length);
        Assert.Equal(20, request.Width);
        Assert.Equal(12, request.Height);
        Assert.Equal(1_000_000, request.InsuranceValue);
    }

    [Fact]
    public async Task Quote_NoConfiguredServiceType_UsesLightGoodsServiceType()
    {
        await SeedCatalogAsync();
        await SeedSettingsAsync(defaultServiceTypeId: null);

        var quote = await QuoteAsync();

        Assert.Equal(2, quote.ServiceTypeId);
        Assert.Equal(2, Assert.Single(_ghn.FeeRequests).ServiceTypeId);
    }

    [Theory]
    [InlineData(4_999_999, 4_999_999)]
    [InlineData(5_000_000, 5_000_000)]
    [InlineData(5_000_001, 5_000_000)]
    [InlineData(12_000_000, 5_000_000)]
    public async Task Quote_InsuredValue_IsCappedAtGhnLimitInRequest(int insuredValue, int expectedSent)
    {
        await SeedCatalogAsync();
        await SeedSettingsAsync(defaultServiceTypeId: null);

        await QuoteAsync(insuredValue: insuredValue);

        Assert.Equal(expectedSent, Assert.Single(_ghn.FeeRequests).InsuranceValue);
    }
}
