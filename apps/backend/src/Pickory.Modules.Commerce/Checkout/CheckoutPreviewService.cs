using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Carts;
using Pickory.Modules.Commerce.Pricing;
using Pickory.Modules.Commerce.Settings;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Addresses;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Checkout;

// Tính tạm tính, phí vận chuyển và tổng tiền cho giỏ hiện tại. Preview chỉ đọc, không ghi DB và không giữ hàng.
public sealed class CheckoutPreviewService(
    CartService carts,
    ICatalogReader catalog,
    CommerceSettingsProvider settings,
    IAddressDirectory addressDirectory,
    IShippingService shipping,
    ILogger<CheckoutPreviewService> logger)
{
    private const string AddressNotFoundMessage = "Địa chỉ nhận hàng không còn trong danh mục. Vui lòng chọn lại.";
    private const string EmptyCartMessage = "Giỏ hàng đang trống.";
    private const string LineUnavailableMessage = "Giỏ hàng có sản phẩm không thể mua. Hãy xóa sản phẩm đó rồi thử lại.";
    private const string MissingDimensionsMessage = "Sản phẩm trong giỏ chưa thể đặt hàng lúc này. Vui lòng liên hệ cửa hàng.";
    private const string CodNotAllowedMessage = "Đơn hàng vượt hạn mức thanh toán khi nhận hàng. Vui lòng chọn chuyển khoản.";

    public async Task<CheckoutPreviewResponse> PreviewAsync(
        CartOwner owner, CheckoutPreviewInput input, CancellationToken cancellationToken)
    {
        // Tên tỉnh và phường lấy từ danh mục, không tin client. Mã sai dừng ở đây trước khi đọc giỏ.
        var names = await addressDirectory.FindAsync(
            input.Address.ProvinceCode, input.Address.WardCode, cancellationToken);
        if (names is null)
        {
            throw new ValidationException([new ValidationFailure(CheckoutPreviewRequests.AddressField, AddressNotFoundMessage)]);
        }

        // Body không mang dòng hàng. Server đọc giỏ của chính người gọi.
        var lines = await carts.GetLinesAsync(owner, cancellationToken);
        if (lines.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("cart", EmptyCartMessage)]);
        }

        var limits = await settings.GetLimitsAsync(cancellationToken);
        var snapshots = await catalog.GetSkusAsync(lines.Select(line => line.SkuId).ToList(), cancellationToken);
        var snapshotsBySkuId = snapshots.ToDictionary(snapshot => snapshot.SkuId);

        // Dùng lại luật của giỏ để biết dòng nào không mua được. Checkout không lặp lại luật trạng thái SKU.
        var cart = CartResponseBuilder.Build(lines, snapshotsBySkuId, limits);
        if (cart.HasUnavailableItems)
        {
            throw new AppException("SKU_UNAVAILABLE", 422, LineUnavailableMessage);
        }

        // Sau bước trên mọi SKU đều có trong catalog, nên tra từ điển không thiếu khóa.
        var checkoutLines = lines
            .Select(line => ToCheckoutLine(line, snapshotsBySkuId[line.SkuId]))
            .ToList();

        // Thiếu cân nặng hoặc kích thước thì không báo phí được. Ghi log để nhân viên bổ sung dữ liệu, và không đoán giá trị.
        var skuIdsMissingDimensions = CheckoutParcelRules.FindSkuIdsMissingDimensions(checkoutLines);
        foreach (var skuId in skuIdsMissingDimensions)
        {
            logger.LogError("Preview bị chặn vì SKU {SkuId} thiếu cân nặng hoặc kích thước trong catalog.", skuId);
        }

        if (skuIdsMissingDimensions.Count > 0)
        {
            throw new AppException("SKU_UNAVAILABLE", 422, MissingDimensionsMessage);
        }

        // Kiện dựng từ số đo của từng SKU. Shipping gộp các dòng thành một kiện để báo phí.
        var parcel = shipping.BuildParcel(CheckoutParcelRules.BuildParcelItems(checkoutLines));

        // Tạm tính tính trước phí vận chuyển, vì giá trị khai giá của báo phí bằng tạm tính.
        var pricedLines = checkoutLines.Select(line => new PricedLine(line.UnitPrice, line.Quantity)).ToList();
        var subtotal = OrderPricing.Calculate(pricedLines, carrierShippingFee: 0, limits.FreeShippingThreshold).Subtotal;

        // Báo phí không thu hộ, vì phí không phụ thuộc phương thức thanh toán. Lỗi của shipping đi thẳng ra ngoài với mã gốc.
        var destination = new ShippingAddress(
            input.Address.ProvinceCode, names.ProvinceName, input.Address.WardCode, names.WardName, input.Address.Line);
        var quote = await shipping.QuoteAsync(destination, parcel, codAmount: 0, insuredValue: subtotal, cancellationToken);

        // Tính tiền với phí thật của quote và ngưỡng miễn phí của cấu hình.
        var pricing = OrderPricing.Calculate(pricedLines, quote.Fee, limits.FreeShippingThreshold);

        // Kiểm COD sau khi có tổng tiền. Tổng chỉ biết được khi đã có phí vận chuyển.
        var codAllowed = OrderPricing.IsCodAllowed(pricing.Total, limits.CodMaxAmount);
        if (input.PaymentMethod == CheckoutPaymentMethods.Cod && !codAllowed)
        {
            throw new AppException("COD_NOT_ALLOWED", 422, CodNotAllowedMessage);
        }

        return new CheckoutPreviewResponse(
            pricing.Subtotal,
            pricing.ShippingFee,
            pricing.ShippingDiscount,
            pricing.CustomerShippingFee,
            pricing.Total,
            limits.FreeShippingThreshold,
            limits.CodMaxAmount,
            codAllowed,
            // Ngày giao dạng ISO, không phụ thuộc văn hóa của máy chủ.
            quote.ExpectedDeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new CheckoutPreviewAddress(
                input.Address.ProvinceCode, names.ProvinceName, input.Address.WardCode, names.WardName, input.Address.Line));
    }

    private static CheckoutLine ToCheckoutLine(CartLineInput line, SkuSnapshot sku) =>
        new(line.SkuId, line.Quantity, sku.Price, sku.WeightGrams, sku.LengthCm, sku.WidthCm, sku.HeightCm);
}
