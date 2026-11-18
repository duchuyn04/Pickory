using Pickory.Modules.Commerce.Payments;
using Pickory.Modules.Commerce.Payments.Admin;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Payments;

// Nhãn tiếng Việt của trạng thái khớp trong file xuất giao dịch SePay, theo ví dụ `payment transactions export success`.
public sealed class PaymentTransactionExportLabelsTests
{
    [Theory]
    [InlineData(PaymentMatchStatuses.Unmatched, "Chưa khớp")]
    [InlineData(PaymentMatchStatuses.Matched, "Đã khớp")]
    [InlineData(PaymentMatchStatuses.Ignored, "Đã bỏ qua")]
    // Mỗi trạng thái đã biết có đúng một nhãn. Đổi chữ của một nhãn làm đúng một dòng đỏ.
    public void ForMatchStatus_KnownValue_ReturnsVietnameseLabel(string matchStatus, string expectedLabel)
    {
        Assert.Equal(expectedLabel, PaymentTransactionExportLabels.ForMatchStatus(matchStatus));
    }

    [Fact]
    // Mọi trạng thái mà route danh sách chấp nhận đều có nhãn khác mã. Thêm trạng thái mới mà quên nhãn thì test này đỏ.
    public void ForMatchStatus_EveryKnownMatchStatus_HasLabelDifferentFromCode()
    {
        foreach (var matchStatus in AdminPaymentTransactionListQuery.KnownMatchStatuses)
        {
            Assert.NotEqual(matchStatus, PaymentTransactionExportLabels.ForMatchStatus(matchStatus));
        }
    }

    [Fact]
    // Giá trị lạ trong DB giữ nguyên chuỗi và không ném lỗi, để file vẫn xuất được.
    public void ForMatchStatus_UnknownValue_ReturnsRawString()
    {
        Assert.Equal("trang_thai_moi", PaymentTransactionExportLabels.ForMatchStatus("trang_thai_moi"));
    }

    [Fact]
    // Bảng nhãn phân biệt hoa thường như điều kiện lọc của route danh sách, nên `MATCHED` là giá trị lạ.
    public void ForMatchStatus_DifferentCase_IsTreatedAsUnknown()
    {
        Assert.Equal("MATCHED", PaymentTransactionExportLabels.ForMatchStatus("MATCHED"));
    }
}
