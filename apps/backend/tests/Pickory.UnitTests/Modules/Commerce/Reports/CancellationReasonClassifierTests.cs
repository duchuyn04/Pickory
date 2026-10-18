using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Reports;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Reports;

// Phân loại lý do hủy cho báo cáo: quá hạn thanh toán, khách tự hủy hoặc shop hủy.
public sealed class CancellationReasonClassifierTests
{
    [Fact]
    // Cờ `payment_expired` là dấu hệ thống ghi khi hết hạn, nên đơn có cờ được xếp vào quá hạn.
    public void Classify_WithPaymentExpiredFlag_IsPaymentExpired()
    {
        var reason = CancellationReasonClassifier.Classify(
            hasPaymentExpiredFlag: true, latestCancellationHistoryReason: "Quá hạn thanh toán.");

        Assert.Equal(CancellationReason.PaymentExpired, reason);
    }

    [Fact]
    // Lý do lịch sử của khách tự hủy phải khớp đúng hằng mà dịch vụ theo dõi đơn ghi.
    public void Classify_WithCustomerCancellationReason_IsCustomer()
    {
        var reason = CancellationReasonClassifier.Classify(
            hasPaymentExpiredFlag: false, OrderReportMarkers.CustomerCancellationReason);

        Assert.Equal(CancellationReason.Customer, reason);
    }

    [Fact]
    // Cờ hết hạn thắng lý do lịch sử, vì cờ là dấu hệ thống còn lý do lịch sử có thể do người nhập.
    public void Classify_FlagAndCustomerReasonTogether_FlagWins()
    {
        var reason = CancellationReasonClassifier.Classify(
            hasPaymentExpiredFlag: true, OrderReportMarkers.CustomerCancellationReason);

        Assert.Equal(CancellationReason.PaymentExpired, reason);
    }

    [Theory]
    [InlineData("Khách đổi ý, gọi điện báo hủy.")]
    [InlineData("khách tự hủy.")]
    [InlineData("")]
    // Lý do khác hằng của khách là shop hủy. So sánh phân biệt hoa thường, vì hằng được ghi nguyên văn.
    public void Classify_WithOtherHistoryReason_IsShop(string historyReason)
    {
        var reason = CancellationReasonClassifier.Classify(hasPaymentExpiredFlag: false, historyReason);

        Assert.Equal(CancellationReason.Shop, reason);
    }

    [Fact]
    // Đơn không có dòng lịch sử hủy, ví dụ dữ liệu nhập tay, được xem là shop hủy.
    public void Classify_WithoutCancellationHistory_IsShop()
    {
        var reason = CancellationReasonClassifier.Classify(
            hasPaymentExpiredFlag: false, latestCancellationHistoryReason: null);

        Assert.Equal(CancellationReason.Shop, reason);
    }
}
