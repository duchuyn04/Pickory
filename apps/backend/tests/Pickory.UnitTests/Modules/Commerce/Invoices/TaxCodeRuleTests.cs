using Pickory.Modules.Commerce.Invoices;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Invoices;

public sealed class TaxCodeRuleTests
{
    // Dấu gạch dài (U+2013 và U+2014) không được viết thẳng vào mã nguồn, nên test dựng ký tự từ mã Unicode.
    private static readonly string EnDash = char.ConvertFromUtf32(0x2013);
    private static readonly string EmDash = char.ConvertFromUtf32(0x2014);

    [Theory]
    [InlineData("0312345678")]
    [InlineData("0312345678001")]
    [InlineData("0312345678-001")]
    [InlineData("031-234-5678")]
    [InlineData("0-3-1-2-3-4-5-6-7-8-0-0-1")]
    // Mười hoặc mười ba chữ số sau khi bỏ dấu gạch thì hợp lệ.
    public void IsValid_WithTenOrThirteenDigits_ReturnsTrue(string taxCode)
    {
        Assert.True(TaxCodeRule.IsValid(taxCode));
    }

    [Theory]
    [InlineData("")]
    [InlineData("031234567")]
    [InlineData("03123456789")]
    [InlineData("031234567800")]
    [InlineData("03123456780011")]
    [InlineData("-------------")]
    // Số chữ số khác 10 và 13 bị từ chối, kể cả khi chuỗi toàn dấu gạch.
    public void IsValid_WithWrongDigitCount_ReturnsFalse(string taxCode)
    {
        Assert.False(TaxCodeRule.IsValid(taxCode));
    }

    [Theory]
    [InlineData("03123456A8")]
    [InlineData("0312 345678")]
    [InlineData(" 0312345678")]
    [InlineData("0312345678 ")]
    [InlineData("0312345678_001")]
    // Chữ cái, khoảng trắng và dấu gạch dưới bị từ chối. Bên gọi tự cắt khoảng trắng đầu và cuối trước khi kiểm.
    public void IsValid_WithForeignCharacters_ReturnsFalse(string taxCode)
    {
        Assert.False(TaxCodeRule.IsValid(taxCode));
    }

    [Fact]
    // Dấu gạch dài của trình soạn thảo văn bản trông giống dấu gạch ASCII. Phần mềm hóa đơn không đọc được, nên luật từ chối.
    public void IsValid_WithTypographicDash_ReturnsFalse()
    {
        Assert.False(TaxCodeRule.IsValid("0312345678" + EnDash + "001"));
        Assert.False(TaxCodeRule.IsValid("0312345678" + EmDash + "001"));
    }

    [Fact]
    // Chữ số của hệ chữ khác, ví dụ chữ số Ả Rập, bị từ chối. Luật chỉ nhận chữ số ASCII.
    public void IsValid_WithNonAsciiDigits_ReturnsFalse()
    {
        var arabicIndicDigits = string.Concat(Enumerable.Range(0x0660, 10).Select(char.ConvertFromUtf32));

        Assert.False(TaxCodeRule.IsValid(arabicIndicDigits));
    }

    [Fact]
    public void IsValid_WithNull_ReturnsFalse()
    {
        Assert.False(TaxCodeRule.IsValid(null));
    }
}
