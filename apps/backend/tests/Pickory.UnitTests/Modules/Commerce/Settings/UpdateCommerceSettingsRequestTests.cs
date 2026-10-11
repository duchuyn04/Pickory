using System.Text.Json;
using FluentValidation.Results;
using Pickory.Modules.Commerce.Settings;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Settings;

// Luật đọc body của PUT cấu hình commerce. Logic thuần, không DB và không HTTP.
public class UpdateCommerceSettingsRequestTests
{
    private const string ValidJson = """
        {"freeShippingThreshold":500000,"codMaxAmount":20000000,"maxQuantityPerSku":5,"maxCartLines":20,"sepayAccountNumber":"0123456789","sepayBankCode":"MB"}
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // Dựng body từ body hợp lệ. Mỗi cặp (trường, JSON thô) thay giá trị trường đó, và `null` ở vế giá trị nghĩa là bỏ hẳn trường.
    private static string BodyWith(params (string Field, string? RawValue)[] changes)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ValidJson)!;
        foreach (var (field, rawValue) in changes)
        {
            if (rawValue is null)
            {
                values.Remove(field);
            }
            else
            {
                values[field] = Parse(rawValue);
            }
        }

        return JsonSerializer.Serialize(values);
    }

    private static (CommerceSettingsInput Input, List<ValidationFailure> Failures) Read(string json)
    {
        var failures = new List<ValidationFailure>();
        var input = UpdateCommerceSettingsRequest.Read(Parse(json), failures);
        return (input, failures);
    }

    private static string[] FailedFields(List<ValidationFailure> failures) =>
        failures.Select(failure => failure.PropertyName).OrderBy(name => name, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Read_ValidBody_ReturnsValues()
    {
        var (input, failures) = Read(ValidJson);

        Assert.Empty(failures);
        Assert.Equal((500_000, 20_000_000, 5, 20, "0123456789", "MB"),
            (input.FreeShippingThreshold, input.CodMaxAmount, input.MaxQuantityPerSku, input.MaxCartLines,
                input.SepayAccountNumber, input.SepayBankCode));
    }

    [Theory]
    [InlineData("freeShippingThreshold", "0", true)]
    [InlineData("freeShippingThreshold", "1000000000", true)]
    [InlineData("freeShippingThreshold", "-1", false)]
    [InlineData("freeShippingThreshold", "1000000001", false)]
    [InlineData("codMaxAmount", "0", true)]
    [InlineData("codMaxAmount", "1000000000", true)]
    [InlineData("codMaxAmount", "-1", false)]
    [InlineData("codMaxAmount", "1000000001", false)]
    [InlineData("maxQuantityPerSku", "1", true)]
    [InlineData("maxQuantityPerSku", "99", true)]
    [InlineData("maxQuantityPerSku", "0", false)]
    [InlineData("maxQuantityPerSku", "100", false)]
    [InlineData("maxCartLines", "1", true)]
    [InlineData("maxCartLines", "100", true)]
    [InlineData("maxCartLines", "0", false)]
    [InlineData("maxCartLines", "101", false)]
    // Biên dưới và biên trên của từng trường số: sát biên trong khoảng thì qua, ngoài khoảng một đơn vị thì lỗi.
    public void Read_NumberBoundary_AcceptsInsideAndRejectsOutside(string field, string rawValue, bool accepted)
    {
        var (_, failures) = Read(BodyWith((field, rawValue)));

        Assert.Equal(accepted ? Array.Empty<string>() : [field], FailedFields(failures));
    }

    [Theory]
    [InlineData("\"5\"")]
    [InlineData("5.5")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[5]")]
    [InlineData("99999999999")]
    // Số nguyên JSON là kiểu duy nhất hợp lệ. Chuỗi, số lẻ, `null` và số tràn `int` đều lỗi.
    public void Read_NumberWithWrongType_ReportsField(string rawValue)
    {
        var (_, failures) = Read(BodyWith(("maxCartLines", rawValue)));

        Assert.Equal(["maxCartLines"], FailedFields(failures));
    }

    [Theory]
    [InlineData("freeShippingThreshold")]
    [InlineData("codMaxAmount")]
    [InlineData("maxQuantityPerSku")]
    [InlineData("maxCartLines")]
    // PUT thay toàn bộ cấu hình, nên thiếu một trường số là lỗi ở đúng trường đó.
    public void Read_MissingNumberField_ReportsField(string field)
    {
        var (_, failures) = Read(BodyWith((field, null)));

        Assert.Equal([field], FailedFields(failures));
    }

    [Theory]
    [InlineData("\"123456\"", "123456")]
    [InlineData("\"12345678901234567890\"", "12345678901234567890")]
    [InlineData("\"  0123456789  \"", "0123456789")]
    // Số tài khoản dài 6 đến 20 chữ số. Khoảng trắng hai đầu bị cắt trước khi kiểm.
    public void Read_ValidAccountNumber_IsTrimmed(string rawValue, string expected)
    {
        var (input, failures) = Read(BodyWith(("sepayAccountNumber", rawValue)));

        Assert.Empty(failures);
        Assert.Equal(expected, input.SepayAccountNumber);
    }

    [Theory]
    [InlineData("\"12345\"")]
    [InlineData("\"123456789012345678901\"")]
    [InlineData("\"12345a\"")]
    [InlineData("\"123 456\"")]
    [InlineData("\"-123456\"")]
    [InlineData("\"١٢٣٤٥٦\"")]
    [InlineData("123456")]
    // Số tài khoản chỉ gồm chữ số ASCII. Số JSON không được ép thành chuỗi.
    public void Read_InvalidAccountNumber_ReportsField(string rawValue)
    {
        var (_, failures) = Read(BodyWith(("sepayAccountNumber", rawValue)));

        Assert.Equal(["sepayAccountNumber"], FailedFields(failures));
    }

    [Theory]
    [InlineData("\"MB\"", "MB")]
    [InlineData("\"mb\"", "MB")]
    [InlineData("\"  vcb  \"", "VCB")]
    [InlineData("\"ACB2\"", "ACB2")]
    [InlineData("\"AAAAAAAAAAAAAAAAAAAA\"", "AAAAAAAAAAAAAAAAAAAA")]
    // Mã ngân hàng lưu dạng chữ hoa, nên chữ thường được đổi sang chữ hoa.
    public void Read_ValidBankCode_IsStoredUppercase(string rawValue, string expected)
    {
        var (input, failures) = Read(BodyWith(("sepayBankCode", rawValue)));

        Assert.Empty(failures);
        Assert.Equal(expected, input.SepayBankCode);
    }

    [Theory]
    [InlineData("\"M\"")]
    [InlineData("\"AAAAAAAAAAAAAAAAAAAAA\"")]
    [InlineData("\"MB-1\"")]
    [InlineData("\"M B\"")]
    [InlineData("\"ÂB\"")]
    [InlineData("12")]
    // Mã ngân hàng dài 2 đến 20 ký tự, chỉ gồm chữ cái và chữ số ASCII.
    public void Read_InvalidBankCode_ReportsField(string rawValue)
    {
        var (_, failures) = Read(BodyWith(("sepayBankCode", rawValue)));

        Assert.Equal(["sepayBankCode"], FailedFields(failures));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    // Chuỗi rỗng, chuỗi trắng và `null` đều là chưa nhập. Cả hai trường SePay cùng trống thì hợp lệ.
    public void Read_BothSepayFieldsEmpty_StoresNulls(string rawValue)
    {
        var (input, failures) = Read(BodyWith(("sepayAccountNumber", rawValue), ("sepayBankCode", rawValue)));

        Assert.Empty(failures);
        Assert.Null(input.SepayAccountNumber);
        Assert.Null(input.SepayBankCode);
    }

    [Fact]
    // Hai trường SePay có thể vắng hẳn trong body, giống `null`.
    public void Read_SepayFieldsAbsent_StoresNulls()
    {
        var (input, failures) = Read(BodyWith(("sepayAccountNumber", null), ("sepayBankCode", null)));

        Assert.Empty(failures);
        Assert.Null(input.SepayAccountNumber);
        Assert.Null(input.SepayBankCode);
    }

    [Theory]
    [InlineData("sepayBankCode")]
    [InlineData("sepayAccountNumber")]
    // Chỉ có một trường SePay thì lỗi nằm ở ô còn thiếu, không nằm ở ô đã nhập.
    public void Read_OnlyOneSepayField_ReportsTheMissingOne(string emptiedField)
    {
        var (_, failures) = Read(BodyWith((emptiedField, "\"  \"")));

        Assert.Equal([emptiedField], FailedFields(failures));
    }

    [Fact]
    // Một ô SePay sai định dạng và ô kia trống: báo đủ cả hai để UI hiện lỗi một lần.
    public void Read_OneInvalidAndOneEmptySepayField_ReportsBoth()
    {
        var (_, failures) = Read(BodyWith(("sepayAccountNumber", "\"abc\""), ("sepayBankCode", "null")));

        Assert.Equal(["sepayAccountNumber", "sepayBankCode"], FailedFields(failures));
    }

    [Fact]
    // Trường lạ bị từ chối theo tên, giống route cấu hình shipping.
    public void Read_UnknownField_ReportsFieldName()
    {
        var (_, failures) = Read(BodyWith(("sepayWebhookApiKey", "\"khoa-bi-mat\"")));

        Assert.Equal(["sepayWebhookApiKey"], FailedFields(failures));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"chuỗi\"")]
    [InlineData("5")]
    [InlineData("null")]
    // Body không phải đối tượng JSON chỉ có một lỗi gắn với cả body.
    public void Read_BodyIsNotAnObject_ReportsSingleBodyFailure(string json)
    {
        var failures = new List<ValidationFailure>();

        UpdateCommerceSettingsRequest.Read(Parse(json), failures);

        Assert.Equal([string.Empty], FailedFields(failures));
    }

    [Fact]
    // Nhiều trường sai thì một lần đọc liệt kê đủ từng trường.
    public void Read_MultipleInvalidFields_ReportsEveryField()
    {
        var body = BodyWith(("freeShippingThreshold", "-5"), ("maxCartLines", "0"), ("sepayBankCode", "\"!\""));

        var (_, failures) = Read(body);

        Assert.Equal(["freeShippingThreshold", "maxCartLines", "sepayBankCode"], FailedFields(failures));
    }
}
