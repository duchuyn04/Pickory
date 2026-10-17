using Pickory.Modules.Commerce.Contracts.Reading;
using Xunit;

namespace Pickory.ArchitectureTests;

// Record mẫu cố ý mang thuộc tính kiểu entity để chứng minh validator bắt được vi phạm.
public sealed record SampleViolatingRecord(
    string Code,
    Pickory.Modules.Commerce.Entities.Order? ViolatingEntity);

// Interface mẫu giả lập hợp đồng để kiểm tra khả năng phát hiện vi phạm kiến trúc.
public interface ISampleViolatingReader
{
    // Phương thức mẫu trả về kiểu chứa thuộc tính entity bị cấm.
    Task<SampleViolatingRecord> GetSampleAsync();
}

// Kiểm tra kiến trúc interface ICommerceReader bảo đảm không lộ entity hay kiểu nội bộ.
public sealed class CommerceReaderArchitectureTests
{
    private const string AllowedContractNamespace = "Pickory.Modules.Commerce.Contracts";

    [Fact]
    // Mọi kiểu dữ liệu trong chữ ký ICommerceReader phải thuộc Contracts hoặc System.
    public void ICommerceReader_ChiTraKieuTrongContracts_KhongLoEntityEfCore()
    {
        // Duyệt toàn diện phương thức, tham số và thuộc tính của các kiểu trả về.
        var violations = InterfaceContractValidator.ValidateInterfaceContracts(
            typeof(ICommerceReader), AllowedContractNamespace);

        // Khẳng định không có bất kỳ kiểu dữ liệu entity hoặc kiểu nội bộ nào bị lộ.
        Assert.True(
            violations.Count == 0,
            $"ICommerceReader để lộ kiểu không hợp lệ:\n{string.Join("\n", violations)}");
    }

    [Fact]
    // Validator phải phát hiện và báo lỗi khi record trong hợp đồng chứa thuộc tính entity.
    public void ValidateInterfaceContracts_PhatHienViPham_KhiRecordChuaEntityEfCore()
    {
        // Duyệt interface mẫu chứa thuộc tính entity để xác minh tính đúng đắn của validator.
        var violations = InterfaceContractValidator.ValidateInterfaceContracts(
            typeof(ISampleViolatingReader), "Pickory.ArchitectureTests");

        // Khẳng định bắt được đúng vi phạm về entity Order.
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("Pickory.Modules.Commerce.Entities.Order"));
    }
}
