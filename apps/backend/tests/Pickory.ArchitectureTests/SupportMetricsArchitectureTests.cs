using Pickory.Modules.SupportDesk.Contracts.Metrics;
using Xunit;

namespace Pickory.ArchitectureTests;

// Kiểm tra kiến trúc interface `ISupportMetrics` bảo đảm không lộ entity hay kiểu nội bộ.
public sealed class SupportMetricsArchitectureTests
{
    private const string AllowedContractNamespace = "Pickory.Modules.SupportDesk.Contracts";

    [Fact]
    // Mọi kiểu dữ liệu trong chữ ký `ISupportMetrics` phải thuộc Contracts hoặc System.
    public void ISupportMetrics_ChiTraKieuTrongContracts_KhongLoEntityEfCore()
    {
        // Dùng lại bộ duyệt chung để luật của các cổng Contracts không lệch nhau.
        var violations = InterfaceContractValidator.ValidateInterfaceContracts(
            typeof(ISupportMetrics), AllowedContractNamespace);

        Assert.True(
            violations.Count == 0,
            $"ISupportMetrics để lộ kiểu không hợp lệ:\n{string.Join("\n", violations)}");
    }

    [Fact]
    // Interface phải có đúng hai hàm số liệu, để test trên không xanh giả vì interface rỗng.
    public void ISupportMetrics_CoDungHaiHamSoLieu()
    {
        var methodNames = typeof(ISupportMetrics).GetMethods().Select(method => method.Name).Order().ToArray();

        Assert.Equal(["GetAsync", "GetPendingCountsAsync"], methodNames);
    }
}
