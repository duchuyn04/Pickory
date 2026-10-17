using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

// Kiểm tra quy tắc xác định đơn hàng đã mua đủ mọi cặp trạng thái.
public sealed class PurchasedOrderRulesTests
{
    private static readonly Func<Order, bool> CompiledRule = PurchasedOrderRules.IsPurchased.Compile();

    // Bảng mong đợi viết tay gồm đúng 8 cặp trạng thái được công nhận là đã mua.
    private static readonly HashSet<(OrderStatus OrderStatus, PaymentStatus PaymentStatus)> AllowedPairs =
    [
        (OrderStatus.Delivered, PaymentStatus.Unpaid),
        (OrderStatus.Delivered, PaymentStatus.Paid),
        (OrderStatus.Delivered, PaymentStatus.RefundPending),
        (OrderStatus.Delivered, PaymentStatus.Refunded),
        (OrderStatus.PendingPayment, PaymentStatus.Paid),
        (OrderStatus.PendingConfirmation, PaymentStatus.Paid),
        (OrderStatus.Confirmed, PaymentStatus.Paid),
        (OrderStatus.Shipping, PaymentStatus.Paid),
    ];

    [Fact]
    // Bảng mong đợi viết tay phải chứa đúng 8 cặp trạng thái thỏa mãn trong 28 cặp.
    public void BangMongDoiVietTay_ChuaDung8CapTrangThaiThoaMan()
    {
        Assert.Equal(8, AllowedPairs.Count);
    }

    public static TheoryData<OrderStatus, PaymentStatus, bool> AllStatusCombinations()
    {
        var data = new TheoryData<OrderStatus, PaymentStatus, bool>();

        // Duyệt đủ 7 OrderStatus và 4 PaymentStatus so sánh với bảng mong đợi viết tay.
        foreach (var orderStatus in Enum.GetValues<OrderStatus>())
        {
            foreach (var paymentStatus in Enum.GetValues<PaymentStatus>())
            {
                var expected = AllowedPairs.Contains((orderStatus, paymentStatus));
                data.Add(orderStatus, paymentStatus, expected);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllStatusCombinations))]
    public void IsPurchased_DuyetDu28CapTrangThai_KhopBangMongDoi(
        OrderStatus orderStatus, PaymentStatus paymentStatus, bool expected)
    {
        // Tạo đối tượng đơn với cặp trạng thái cần kiểm tra.
        var order = new Order
        {
            Status = orderStatus,
            PaymentStatus = paymentStatus,
        };

        // Thực thi biểu thức đã biên dịch lên đối tượng đơn.
        var actual = CompiledRule(order);

        Assert.Equal(expected, actual);
    }
}
