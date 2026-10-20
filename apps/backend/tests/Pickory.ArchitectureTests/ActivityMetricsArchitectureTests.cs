using Pickory.Modules.ActivityTracking.Contracts.Metrics;
using Xunit;

namespace Pickory.ArchitectureTests;

// Kiểm tra kiến trúc interface IActivityMetrics bảo đảm không lộ entity hay kiểu nội bộ.
public sealed class ActivityMetricsArchitectureTests
{
    private const string AllowedContractNamespace = "Pickory.Modules.ActivityTracking.Contracts";

    [Fact]
    // Mọi kiểu dữ liệu trong chữ ký IActivityMetrics phải thuộc Contracts hoặc System.
    public void IActivityMetrics_ChiTraKieuTrongContracts_KhongLoEntityEfCore()
    {
        // Dùng lại bộ duyệt chung để luật của các cổng Contracts không lệch nhau.
        var violations = InterfaceContractValidator.ValidateInterfaceContracts(
            typeof(IActivityMetrics), AllowedContractNamespace);

        Assert.True(
            violations.Count == 0,
            $"IActivityMetrics để lộ kiểu không hợp lệ:\n{string.Join("\n", violations)}");
    }

    [Fact]
    // Interface phải có đúng một hàm phễu, để test trên không xanh giả vì interface rỗng.
    public void IActivityMetrics_CoDungMotHamTinhPheu()
    {
        var methodNames = typeof(IActivityMetrics).GetMethods().Select(method => method.Name).ToArray();

        Assert.Equal(["GetFunnelAsync"], methodNames);
    }
}
