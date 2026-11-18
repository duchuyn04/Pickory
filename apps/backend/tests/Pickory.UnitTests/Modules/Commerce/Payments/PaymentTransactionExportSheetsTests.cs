using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Payments;
using Pickory.Modules.Commerce.Payments.Admin;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Payments;

// Dựng dòng hiển thị của sheet "Giao dịch SePay" từ giao dịch đọc từ DB.
public sealed class PaymentTransactionExportSheetsTests
{
    private static readonly DateTime ReceivedAt = new(2026, 11, 9, 3, 20, 0, DateTimeKind.Utc);

    private static PaymentTransactionExportSource Source(
        string matchStatus = PaymentMatchStatuses.Unmatched,
        string? orderCode = null,
        int? orderTotal = null,
        PaymentStatus? orderPaymentStatus = null,
        IReadOnlyList<string>? orderFlags = null) =>
        new("FT26313123456", ReceivedAt, 25_370_000, "CK tien hang thang 11", matchStatus, orderCode, orderTotal, orderPaymentStatus, orderFlags);

    [Fact]
    // Giao dịch chưa gắn đơn giữ bốn trường đơn là null. Null làm ô Excel để trống, không phải số 0 hay chuỗi rỗng.
    public void BuildRow_TransactionWithoutOrder_LeavesFourOrderFieldsNull()
    {
        var row = PaymentTransactionExportSheets.BuildRow(Source());

        Assert.Equal("FT26313123456", row.ProviderTxId);
        Assert.Equal(ReceivedAt, row.ReceivedAt);
        Assert.Equal(25_370_000, row.Amount);
        Assert.Equal("CK tien hang thang 11", row.Content);
        Assert.Equal("Chưa khớp", row.MatchStatus);
        Assert.Null(row.OrderCode);
        Assert.Null(row.OrderTotal);
        Assert.Null(row.OrderPaymentStatus);
        Assert.Null(row.OrderNote);
    }

    [Fact]
    // Giao dịch gắn đơn có mã đơn, tổng tiền, nhãn trạng thái thanh toán và ghi chú nối từ nhãn của các cờ.
    public void BuildRow_TransactionWithOrder_FillsOrderFieldsWithVietnameseLabels()
    {
        var row = PaymentTransactionExportSheets.BuildRow(Source(
            PaymentMatchStatuses.Matched, "PK7KX2M9QD", 25_000_000, PaymentStatus.Paid, ["late_payment", "overpaid"]));

        Assert.Equal("Đã khớp", row.MatchStatus);
        Assert.Equal("PK7KX2M9QD", row.OrderCode);
        Assert.Equal(25_000_000, row.OrderTotal);
        Assert.Equal("Đã thanh toán", row.OrderPaymentStatus);
        Assert.Equal("Thanh toán trễ, Thanh toán dư", row.OrderNote);
    }

    [Fact]
    // Đơn không có cờ thì ghi chú để trống, nhưng các trường khác của đơn vẫn có giá trị.
    public void BuildRow_OrderWithoutFlags_LeavesNoteNullButKeepsOtherOrderFields()
    {
        var row = PaymentTransactionExportSheets.BuildRow(Source(
            PaymentMatchStatuses.Matched, "PK7KX2M9QD", 25_000_000, PaymentStatus.Unpaid, []));

        Assert.Equal("PK7KX2M9QD", row.OrderCode);
        Assert.Equal("Chưa thanh toán", row.OrderPaymentStatus);
        Assert.Null(row.OrderNote);
    }

    [Fact]
    // Đơn có tổng tiền bằng 0 vẫn ghi số 0. Chỉ giao dịch chưa gắn đơn mới để trống ô tổng tiền.
    public void BuildRow_OrderWithZeroTotal_KeepsZero()
    {
        var row = PaymentTransactionExportSheets.BuildRow(Source(
            PaymentMatchStatuses.Matched, "PK0", 0, PaymentStatus.Paid, []));

        Assert.Equal(0, row.OrderTotal);
    }
}
