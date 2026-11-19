using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Orders.Admin;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

// Nhãn tiếng Việt của file xuất đơn hàng. Chữ khớp `order-labels.ts` của frontend, trừ nhãn phương thức thanh toán theo spec.
public sealed class OrderExportLabelsTests
{
    private static readonly IReadOnlyDictionary<OrderStatus, string> ExpectedOrderStatusLabels = new Dictionary<OrderStatus, string>
    {
        [OrderStatus.PendingPayment] = "Chờ thanh toán",
        [OrderStatus.PendingConfirmation] = "Chờ xác nhận",
        [OrderStatus.Confirmed] = "Đã xác nhận",
        [OrderStatus.Shipping] = "Đang giao hàng",
        [OrderStatus.Delivered] = "Đã giao hàng",
        [OrderStatus.Returned] = "Đã trả hàng",
        [OrderStatus.Cancelled] = "Đã hủy",
    };

    private static readonly IReadOnlyDictionary<PaymentStatus, string> ExpectedPaymentStatusLabels = new Dictionary<PaymentStatus, string>
    {
        [PaymentStatus.Unpaid] = "Chưa thanh toán",
        [PaymentStatus.Paid] = "Đã thanh toán",
        [PaymentStatus.RefundPending] = "Chờ hoàn tiền",
        [PaymentStatus.Refunded] = "Đã hoàn tiền",
    };

    private static readonly IReadOnlyDictionary<PaymentMethod, string> ExpectedPaymentMethodLabels = new Dictionary<PaymentMethod, string>
    {
        [PaymentMethod.Cod] = "COD",
        [PaymentMethod.Sepay] = "Chuyển khoản SePay",
    };

    private static readonly IReadOnlyDictionary<string, string> ExpectedFlagLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["underpaid"] = "Thanh toán thiếu",
        ["overpaid"] = "Thanh toán dư",
        ["late_payment"] = "Thanh toán trễ",
        ["payment_expired"] = "Quá hạn thanh toán",
        ["invoice_issued"] = "Đã xuất hóa đơn",
        ["shipment_cancelled"] = "Vận đơn đã hủy",
        ["manual_shipping"] = "Giao hàng thủ công",
        ["manual_delivery"] = "Xác nhận giao thủ công",
        ["manual_return"] = "Ghi nhận hoàn thủ công",
        ["demo_order"] = "Đơn mẫu",
    };

    public static TheoryData<OrderStatus> AllOrderStatuses => [.. Enum.GetValues<OrderStatus>()];

    public static TheoryData<PaymentStatus> AllPaymentStatuses => [.. Enum.GetValues<PaymentStatus>()];

    public static TheoryData<PaymentMethod> AllPaymentMethods => [.. Enum.GetValues<PaymentMethod>()];

    // Cờ do backend ghi gồm mọi cờ của danh sách lọc và cờ đơn mẫu. Cờ mới thêm vào `KnownFlags` làm test này đỏ nếu thiếu nhãn.
    public static TheoryData<string> AllFlags => [.. AdminOrderListQuery.KnownFlags.Append("demo_order")];

    [Theory]
    [MemberData(nameof(AllOrderStatuses))]
    // Thêm giá trị `OrderStatus` mới mà quên nhãn thì khóa không có trong bảng mong đợi và test đỏ.
    public void ForOrderStatus_EveryValue_HasExpectedVietnameseLabel(OrderStatus status)
    {
        Assert.Equal(ExpectedOrderStatusLabels[status], OrderExportLabels.ForOrderStatus(status));
    }

    [Theory]
    [MemberData(nameof(AllPaymentStatuses))]
    public void ForPaymentStatus_EveryValue_HasExpectedVietnameseLabel(PaymentStatus status)
    {
        Assert.Equal(ExpectedPaymentStatusLabels[status], OrderExportLabels.ForPaymentStatus(status));
    }

    [Theory]
    [MemberData(nameof(AllPaymentMethods))]
    // Hai nhãn theo spec: `COD` và `Chuyển khoản SePay`, không theo nhãn dài của frontend.
    public void ForPaymentMethod_EveryValue_HasExpectedLabel(PaymentMethod method)
    {
        Assert.Equal(ExpectedPaymentMethodLabels[method], OrderExportLabels.ForPaymentMethod(method));
    }

    [Theory]
    [MemberData(nameof(AllFlags))]
    // Mọi cờ backend ghi đều có nhãn tiếng Việt khác mã cờ.
    public void ForFlag_EveryKnownFlag_HasExpectedVietnameseLabel(string flag)
    {
        Assert.Equal(ExpectedFlagLabels[flag], OrderExportLabels.ForFlag(flag));
        Assert.NotEqual(flag, OrderExportLabels.ForFlag(flag));
    }

    [Fact]
    // Cờ ngoài bảng giữ nguyên mã và không ném lỗi, để file vẫn xuất được.
    public void ForFlag_UnknownFlag_ReturnsRawCode()
    {
        Assert.Equal("flag_moi", OrderExportLabels.ForFlag("flag_moi"));
    }

    [Fact]
    // Enum mới chưa có nhãn rơi về nhãn snake_case thay vì làm hỏng cả file.
    public void ForOrderStatus_ValueWithoutLabel_FallsBackToSnakeCase()
    {
        Assert.Equal("99", OrderExportLabels.ForOrderStatus((OrderStatus)99));
    }

    [Fact]
    // Không có cờ nào thì ô ghi chú để trống, tức `null`.
    public void BuildOrderNote_NoFlags_ReturnsNull()
    {
        Assert.Null(OrderExportLabels.BuildOrderNote([]));
    }

    [Fact]
    // Cờ hóa đơn đã có cột riêng, nên đơn chỉ có cờ này thì ghi chú để trống.
    public void BuildOrderNote_OnlyInvoiceIssued_ReturnsNull()
    {
        Assert.Null(OrderExportLabels.BuildOrderNote(["invoice_issued"]));
    }

    [Fact]
    // Ghi chú giữ thứ tự cờ trong đơn, bỏ cờ hóa đơn và nối bằng `, `.
    public void BuildOrderNote_MixedFlags_JoinsRemainingLabelsInOrder()
    {
        var note = OrderExportLabels.BuildOrderNote(["late_payment", "invoice_issued", "overpaid"]);

        Assert.Equal("Thanh toán trễ, Thanh toán dư", note);
    }

    [Fact]
    // Cờ lạ giữ nguyên mã trong ghi chú.
    public void BuildOrderNote_UnknownFlag_KeepsRawCode()
    {
        Assert.Equal("flag_moi", OrderExportLabels.BuildOrderNote(["flag_moi"]));
    }

    [Theory]
    [InlineData(new[] { "invoice_issued" }, true)]
    [InlineData(new[] { "underpaid", "invoice_issued" }, true)]
    [InlineData(new[] { "underpaid" }, false)]
    [InlineData(new string[0], false)]
    // Cột "Đã xuất hóa đơn VAT" chỉ phụ thuộc cờ `invoice_issued`.
    public void HasInvoiceIssued_ReflectsFlag(string[] flags, bool expected)
    {
        Assert.Equal(expected, OrderExportLabels.HasInvoiceIssued(flags));
    }
}
