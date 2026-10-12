using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class OrderConfirmationEmailTests
{
    private const string WebOrigin = "https://shop.example.com";

    private static OrderConfirmationInput BuildInput(
        int shippingDiscount = 0,
        string orderCode = "PK-ABC123",
        string accessToken = "tok_en-123",
        PaymentMethod paymentMethod = PaymentMethod.Cod,
        IReadOnlyList<OrderConfirmationLine>? lines = null) =>
        new(
            orderCode,
            "an@example.com",
            lines ?? [new OrderConfirmationLine("Laptop Kaze", 2, 20_000_000), new OrderConfirmationLine("Chuột Pickory", 1, 500_000)],
            Subtotal: 40_500_000,
            ShippingFee: 47_000,
            ShippingDiscount: shippingDiscount,
            Total: 40_547_000 - shippingDiscount,
            paymentMethod,
            accessToken);

    [Fact]
    public void Build_AddressesTheContactEmailWithSubjectAndMetadata()
    {
        var message = OrderConfirmationEmail.Build(BuildInput(), WebOrigin);

        Assert.Equal("an@example.com", message.To);
        Assert.Equal("Pickory: xác nhận đơn hàng PK-ABC123", message.Subject);
        Assert.Equal("order-confirmation", message.Metadata["purpose"]);
        Assert.Equal("PK-ABC123", message.Metadata["orderCode"]);
    }

    [Fact]
    public void Build_MetadataDoesNotCarryTheToken()
    {
        var message = OrderConfirmationEmail.Build(BuildInput(), WebOrigin);

        Assert.DoesNotContain(message.Metadata.Values, value => value.Contains("tok_en-123"));
        Assert.Equal(2, message.Metadata.Count);
    }

    [Fact]
    public void Build_BodyListsEveryLineWithNameQuantityAndUnitPrice()
    {
        var text = OrderConfirmationEmail.Build(BuildInput(), WebOrigin).Text;

        Assert.Contains("Mã đơn hàng: PK-ABC123", text);
        Assert.Contains("Laptop Kaze", text);
        Assert.Contains("Số lượng: 2", text);
        Assert.Contains("Đơn giá: 20.000.000 đ", text);
        Assert.Contains("Chuột Pickory", text);
        Assert.Contains("Đơn giá: 500.000 đ", text);
    }

    [Fact]
    public void Build_BodyShowsSubtotalShippingTotalAndPaymentMethod()
    {
        var text = OrderConfirmationEmail.Build(BuildInput(), WebOrigin).Text;

        Assert.Contains("Tạm tính: 40.500.000 đ", text);
        Assert.Contains("Phí vận chuyển: 47.000 đ", text);
        Assert.Contains("Tổng tiền: 40.547.000 đ", text);
        Assert.Contains("Phương thức thanh toán: Thanh toán khi nhận hàng (COD)", text);
    }

    [Fact]
    public void Build_NamesTheTransferPaymentMethod()
    {
        var text = OrderConfirmationEmail.Build(BuildInput(paymentMethod: PaymentMethod.Sepay), WebOrigin).Text;

        Assert.Contains("Phương thức thanh toán: Chuyển khoản (VietQR)", text);
    }

    [Fact]
    public void Build_OmitsTheFreeShippingLineWhenThereIsNoDiscount()
    {
        var text = OrderConfirmationEmail.Build(BuildInput(shippingDiscount: 0), WebOrigin).Text;

        Assert.DoesNotContain("Miễn phí vận chuyển", text);
    }

    [Fact]
    public void Build_ShowsTheFreeShippingLineWhenDiscountIsPositive()
    {
        var text = OrderConfirmationEmail.Build(BuildInput(shippingDiscount: 47_000), WebOrigin).Text;

        Assert.Contains("Miễn phí vận chuyển: -47.000 đ", text);
        Assert.Contains("Tổng tiền: 40.500.000 đ", text);
    }

    [Fact]
    public void Build_FormatsSmallAndZeroAmounts()
    {
        var input = BuildInput(lines: [new OrderConfirmationLine("Cáp", 1, 999)]) with { Subtotal = 0 };

        var text = OrderConfirmationEmail.Build(input, WebOrigin).Text;

        Assert.Contains("Đơn giá: 999 đ", text);
        Assert.Contains("Tạm tính: 0 đ", text);
    }

    [Fact]
    public void Build_LinkHasOriginOrderCodeAndToken()
    {
        var text = OrderConfirmationEmail.Build(BuildInput(), WebOrigin).Text;

        Assert.Contains("https://shop.example.com/orders/PK-ABC123?t=tok_en-123", text);
    }

    [Fact]
    public void Build_EscapesOrderCodeAndTokenInTheLink()
    {
        var input = BuildInput(orderCode: "PK 1/2", accessToken: "a+b/c=&d");

        var text = OrderConfirmationEmail.Build(input, WebOrigin).Text;

        Assert.Contains("/orders/PK%201%2F2?t=a%2Bb%2Fc%3D%26d", text);
    }

    [Fact]
    public void Build_IgnoresATrailingSlashInTheWebOrigin()
    {
        var text = OrderConfirmationEmail.Build(BuildInput(), WebOrigin + "/").Text;

        Assert.Contains("https://shop.example.com/orders/PK-ABC123?t=", text);
        Assert.DoesNotContain("com//orders", text);
    }
}
