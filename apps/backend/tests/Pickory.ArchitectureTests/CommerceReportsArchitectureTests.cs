using Pickory.Modules.Commerce.Contracts.Reports;
using Xunit;

namespace Pickory.ArchitectureTests;

// Kiểm tra kiến trúc interface ICommerceReports bảo đảm không lộ entity hay kiểu nội bộ.
public sealed class CommerceReportsArchitectureTests
{
    private const string AllowedContractNamespace = "Pickory.Modules.Commerce.Contracts";

    [Fact]
    // Mọi kiểu dữ liệu trong chữ ký ICommerceReports phải thuộc Contracts hoặc System.
    public void ICommerceReports_ChiTraKieuTrongContracts_KhongLoEntityEfCore()
    {
        // Dùng lại bộ duyệt chung để quy tắc của hai cổng ICommerceReader và ICommerceReports không lệch nhau.
        var violations = InterfaceContractValidator.ValidateInterfaceContracts(
            typeof(ICommerceReports), AllowedContractNamespace);

        // Khẳng định không có kiểu entity hay kiểu nội bộ nào bị lộ qua tham số, giá trị trả về hoặc thuộc tính.
        Assert.True(
            violations.Count == 0,
            $"ICommerceReports để lộ kiểu không hợp lệ:\n{string.Join("\n", violations)}");
    }

    [Fact]
    // Interface phải có đủ ba hàm báo cáo, để test trên không xanh giả vì interface rỗng.
    public void ICommerceReports_CoDuBaHamBaoCao()
    {
        var methodNames = typeof(ICommerceReports).GetMethods().Select(method => method.Name).Order().ToArray();

        Assert.Equal(
            ["GetDailySalesAsync", "GetPendingCountsAsync", "GetProductSalesAsync"],
            methodNames);
    }
}
