using Pickory.Shared.Validation;
using Xunit;

namespace Pickory.UnitTests.Shared.Validation;

// Luật tên và số điện thoại dùng chung cho hồ sơ người dùng và địa chỉ lấy hàng của shop.
public class ContactFieldRulesTests
{
    private const string NameTooLong = "Họ và tên tối đa 100 ký tự.";
    private const string NameInvalid = "Họ và tên chứa ký tự không hợp lệ.";
    private const string PhoneInvalid = "Số điện thoại không đúng định dạng.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nguyễn Văn A")]
    [InlineData("  Nguyễn Văn A  ")]
    // Tên không bắt buộc. Giá trị trống vẫn hợp lệ vì nó xóa tên đã lưu.
    public void GetNameError_AcceptsEmptyAndNormalNames(string? name)
    {
        Assert.Null(ContactFieldRules.GetNameError(name));
    }

    [Fact]
    // Đúng 100 ký tự có dấu là hợp lệ. Đếm theo ký tự người đọc thấy, không theo byte.
    public void GetNameError_AcceptsExactlyOneHundredVietnameseCharacters()
    {
        var name = string.Concat(Enumerable.Repeat("Ệ", 100));

        Assert.Null(ContactFieldRules.GetNameError(name));
    }

    [Fact]
    // Chữ có dấu viết dạng tổ hợp (chữ E cộng dấu) vẫn tính là một ký tự.
    public void GetNameError_CountsCombinedMarksAsOneCharacter()
    {
        var name = string.Concat(Enumerable.Repeat("E\u0323\u0302", 100));

        Assert.Null(ContactFieldRules.GetNameError(name));
    }

    [Fact]
    public void GetNameError_RejectsOneHundredAndOneCharacters()
    {
        Assert.Equal(NameTooLong, ContactFieldRules.GetNameError(new string('a', 101)));
    }

    [Fact]
    // Khoảng trắng hai đầu không tính vào độ dài.
    public void GetNameError_IgnoresOuterWhitespaceWhenCountingLength()
    {
        Assert.Null(ContactFieldRules.GetNameError("  " + new string('a', 100) + "  "));
    }

    [Theory]
    [InlineData("Tên\u0000x")]
    [InlineData("Tên\nxuống dòng")]
    [InlineData("Tên\ttab")]
    [InlineData("Tên\u001Fx")]
    [InlineData("Tên\u007Fx")]
    public void GetNameError_RejectsControlCharacters(string name)
    {
        Assert.Equal(NameInvalid, ContactFieldRules.GetNameError(name));
    }

    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("0912 345 678", "0912345678")]
    [InlineData("091.234.5678", "0912345678")]
    [InlineData("091-234-5678", "0912345678")]
    [InlineData("+84912345678", "0912345678")]
    [InlineData("+84 912.345.678", "0912345678")]
    [InlineData("0312345678", "0312345678")]
    [InlineData("0512345678", "0512345678")]
    [InlineData("0712345678", "0712345678")]
    [InlineData("0812345678", "0812345678")]
    // Mọi dạng hợp lệ lưu về một dạng duy nhất `0xxxxxxxxx`.
    public void NormalizePhone_ReturnsCanonicalForm(string input, string expected)
    {
        Assert.Null(ContactFieldRules.GetPhoneError(input));
        Assert.Equal(expected, ContactFieldRules.NormalizePhone(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // Số điện thoại không bắt buộc. Giá trị trống xóa số đã lưu.
    public void Phone_EmptyIsValidAndNormalizesToNull(string? input)
    {
        Assert.Null(ContactFieldRules.GetPhoneError(input));
        Assert.Null(ContactFieldRules.NormalizePhone(input));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("0123456789")]
    [InlineData("+8412345678")]
    [InlineData("0212345678")]
    [InlineData("0412345678")]
    [InlineData("0612345678")]
    [InlineData("091234567")]
    [InlineData("09123456789")]
    [InlineData("+840912345678")]
    [InlineData("84912345678")]
    [InlineData("0912a45678")]
    [InlineData("+0912345678")]
    [InlineData("٠٩١٢٣٤٥٦٧٨")]
    public void GetPhoneError_RejectsInvalidNumbers(string input)
    {
        Assert.Equal(PhoneInvalid, ContactFieldRules.GetPhoneError(input));
    }
}
