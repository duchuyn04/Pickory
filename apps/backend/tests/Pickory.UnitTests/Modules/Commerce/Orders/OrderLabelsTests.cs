using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class OrderLabelsTests
{
    [Theory]
    [InlineData(OrderStatus.PendingPayment, "pending_payment")]
    [InlineData(OrderStatus.PendingConfirmation, "pending_confirmation")]
    [InlineData(OrderStatus.Cancelled, "cancelled")]
    public void ToLabel_OrderStatus_IsSnakeCase(OrderStatus status, string expected)
    {
        Assert.Equal(expected, OrderLabels.ToLabel(status));
    }

    [Theory]
    [InlineData(PaymentStatus.Unpaid, "unpaid")]
    [InlineData(PaymentStatus.RefundPending, "refund_pending")]
    public void ToLabel_PaymentStatus_IsSnakeCase(PaymentStatus status, string expected)
    {
        Assert.Equal(expected, OrderLabels.ToLabel(status));
    }

    [Theory]
    [InlineData(PaymentMethod.Cod, "cod")]
    [InlineData(PaymentMethod.Sepay, "sepay")]
    public void ToLabel_PaymentMethod_IsSnakeCase(PaymentMethod method, string expected)
    {
        Assert.Equal(expected, OrderLabels.ToLabel(method));
    }

    // Mỗi giá trị enum phải có nhãn. Nhãn thiếu làm response lệch với dữ liệu đã lưu.
    [Fact]
    public void ToLabel_EveryEnumValue_ReturnsNonEmptyLabel()
    {
        Assert.All(Enum.GetValues<OrderStatus>(), status => Assert.NotEmpty(OrderLabels.ToLabel(status)));
        Assert.All(Enum.GetValues<PaymentStatus>(), status => Assert.NotEmpty(OrderLabels.ToLabel(status)));
        Assert.All(Enum.GetValues<PaymentMethod>(), method => Assert.NotEmpty(OrderLabels.ToLabel(method)));
    }
}
