using Pickory.Modules.Commerce.Payments.Admin;
using Pickory.Shared.Http;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Payments;

// Bộ lọc ghi vào nhật ký của route xuất giao dịch. Khóa là tên tham số của query.
public sealed class PaymentTransactionExportAuditFiltersTests
{
    private static AdminPaymentTransactionListQuery Filter(string? matchStatus, string? orderFlag) =>
        new(new PageQuery(1, 20), matchStatus, orderFlag);

    [Fact]
    // Không có tham số lọc thì nhật ký ghi object rỗng, không ghi `page` hay `limit`.
    public void From_NoFilters_ReturnsEmpty()
    {
        Assert.Empty(PaymentTransactionExportAuditFilters.From(Filter(null, null)));
    }

    [Fact]
    // Chỉ tham số có giá trị được ghi, với đúng tên tham số của query.
    public void From_OnlyMatchStatus_RecordsOnlyThatParameter()
    {
        var filters = PaymentTransactionExportAuditFilters.From(Filter("unmatched", null));

        Assert.Equal(new Dictionary<string, string> { ["matchStatus"] = "unmatched" }, filters);
    }

    [Fact]
    public void From_BothFilters_RecordsBothParametersByQueryName()
    {
        var filters = PaymentTransactionExportAuditFilters.From(Filter("matched", "underpaid"));

        Assert.Equal(
            new Dictionary<string, string> { ["matchStatus"] = "matched", ["orderFlag"] = "underpaid" },
            filters);
    }
}
