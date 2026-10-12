using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class OrderInitialStateTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void For_Cod_IsPendingConfirmationUnpaidWithoutDeadline()
    {
        var state = OrderInitialState.For("cod", Now);

        Assert.Equal(OrderStatus.PendingConfirmation, state.Status);
        Assert.Equal(PaymentStatus.Unpaid, state.PaymentStatus);
        Assert.Null(state.PaymentDeadline);
    }

    [Fact]
    public void For_Sepay_IsPendingPaymentUnpaidWithThirtyMinuteDeadline()
    {
        var state = OrderInitialState.For("sepay", Now);

        Assert.Equal(OrderStatus.PendingPayment, state.Status);
        Assert.Equal(PaymentStatus.Unpaid, state.PaymentStatus);
        Assert.Equal(Now.AddMinutes(30), state.PaymentDeadline);
    }

    [Fact]
    public void For_UnknownMethod_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderInitialState.For("momo", Now));
    }
}
