using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Payments;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Payments;

public sealed class TransferContentParserTests
{
    [Theory]
    [InlineData("PK7KX2M9QD", "PK7KX2M9QD")]
    [InlineData("  PK7KX2M9QD  ", "PK7KX2M9QD")]
    [InlineData("pk7kx2m9qd", "PK7KX2M9QD")]
    [InlineData("Pk7Kx2M9qD", "PK7KX2M9QD")]
    // Khoảng trắng quanh mã và chữ thường không làm mất mã. Kết quả luôn viết hoa.
    public void FindOrderCode_WithWhitespaceOrLowercase_ReturnsUppercaseCode(string content, string expected)
    {
        Assert.Equal(expected, TransferContentParser.FindOrderCode(content));
    }

    [Theory]
    [InlineData("MBVCB.5312345678.PK7KX2M9QD.CT tu NGUYEN VAN A", "PK7KX2M9QD")]
    [InlineData("NGUYEN VAN A chuyen khoan PK7KX2M9QD FT26284123456", "PK7KX2M9QD")]
    [InlineData("TKTHEBVCBPK7KX2M9QD", "PK7KX2M9QD")]
    [InlineData("PK7KX2M9QD-FT26284", "PK7KX2M9QD")]
    [InlineData("thanh toan don hang pk7kx2m9qd cam on", "PK7KX2M9QD")]
    // Ngân hàng chèn chữ trước và sau, kể cả dính liền. Mã vẫn được tìm thấy.
    public void FindOrderCode_WithBankTextAroundCode_ReturnsCode(string content, string expected)
    {
        Assert.Equal(expected, TransferContentParser.FindOrderCode(content));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Chuyen tien an trua")]
    [InlineData("PK7KX2M9Q")]
    [InlineData("PK 7KX2M9QD")]
    [InlineData("PK7KX2M9-QD")]
    [InlineData("PK10000000")]
    [InlineData("XX7KX2M9QD")]
    // Thiếu ký tự, có khoảng trắng giữa mã, hoặc chứa `0` và `1` thì không phải mã đơn. Hàm trả `null`, không ném lỗi.
    public void FindOrderCode_WithoutCode_ReturnsNull(string content)
    {
        Assert.Null(TransferContentParser.FindOrderCode(content));
    }

    [Fact]
    public void FindOrderCode_WithNull_ReturnsNull()
    {
        Assert.Null(TransferContentParser.FindOrderCode(null));
    }

    [Fact]
    // Mã sinh ra bởi bộ sinh mã đơn luôn được parser nhận lại nguyên vẹn.
    public void FindOrderCode_RoundTripsEveryGeneratedCode()
    {
        var random = new Random(11);
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var code = OrderCodeGenerator.Generate(random.Next);

            Assert.Equal(code, TransferContentParser.FindOrderCode($"MB {code.ToLowerInvariant()} FT99"));
        }
    }

    [Fact]
    // Nội dung có hai mã thì hàm trả mã xuất hiện trước. Quyết định này được ghi trong báo cáo.
    public void FindOrderCode_WithTwoCodes_ReturnsTheFirst()
    {
        Assert.Equal("PK22222222", TransferContentParser.FindOrderCode("PK22222222 PK33333333"));
    }
}
