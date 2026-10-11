using System.Text.Json;
using FluentValidation.Results;
using Pickory.Modules.Shipping.Features.AdminSettings;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Settings;

// Luật đọc body của PUT cấu hình shipping. Logic thuần, không DB và không HTTP.
public class UpdateShippingSettingsRequestTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string ValidJson = """
        {"senderName":"Kho Pickory","senderPhone":"0912345678","provinceCode":"01","wardCode":"0101","line":"12 Phố Test","defaultServiceTypeId":2}
        """;

    [Fact]
    public void Read_ValidBody_ReturnsTrimmedAndNormalizedValues()
    {
        var failures = new List<ValidationFailure>();
        var body = Parse("""
            {"senderName":"  Kho Pickory ","senderPhone":"+84 912.345.678","provinceCode":" 01 ","wardCode":"0101","line":"  12 Phố Test  ","defaultServiceTypeId":5}
            """);

        var input = UpdateShippingSettingsRequest.Read(body, failures);

        Assert.Empty(failures);
        Assert.Equal(("Kho Pickory", "0912345678", "01", "0101", "12 Phố Test", 5),
            (input.SenderName, input.SenderPhone, input.ProvinceCode, input.WardCode, input.Line, input.DefaultServiceTypeId));
    }

    [Theory]
    [InlineData("""{"senderName":"Kho Pickory","senderPhone":"0912345678","provinceCode":"01","wardCode":"0101","line":"12 Phố Test"}""")]
    [InlineData("""{"senderName":"Kho Pickory","senderPhone":"0912345678","provinceCode":"01","wardCode":"0101","line":"12 Phố Test","defaultServiceTypeId":null}""")]
    // Thiếu trường hoặc `null` đều nghĩa là chưa chọn gói dịch vụ.
    public void Read_MissingOrNullServiceType_IsNull(string json)
    {
        var failures = new List<ValidationFailure>();

        var input = UpdateShippingSettingsRequest.Read(Parse(json), failures);

        Assert.Empty(failures);
        Assert.Null(input.DefaultServiceTypeId);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("2.5")]
    [InlineData("\"2\"")]
    [InlineData("true")]
    [InlineData("[2]")]
    public void Read_InvalidServiceType_ReportsDefaultServiceTypeId(string rawValue)
    {
        var failures = new List<ValidationFailure>();
        var json = ValidJson.Replace("\"defaultServiceTypeId\":2", $"\"defaultServiceTypeId\":{rawValue}");

        UpdateShippingSettingsRequest.Read(Parse(json), failures);

        Assert.Equal(["defaultServiceTypeId"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_LineWithExactly200Characters_IsAccepted()
    {
        var failures = new List<ValidationFailure>();
        var json = ValidJson.Replace("12 Phố Test", new string('a', 200));

        UpdateShippingSettingsRequest.Read(Parse(json), failures);

        Assert.Empty(failures);
    }

    [Fact]
    // Chữ có dấu dạng tổ hợp (E cộng hai dấu) vẫn tính là một ký tự, nên 200 chữ như vậy hợp lệ.
    public void Read_LineCountsCombinedMarksAsOneCharacter()
    {
        var failures = new List<ValidationFailure>();
        var combined = string.Concat(Enumerable.Repeat("Ệ", 200));
        var json = ValidJson.Replace("12 Phố Test", combined);

        UpdateShippingSettingsRequest.Read(Parse(json), failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void Read_LineWith201Characters_ReportsLine()
    {
        var failures = new List<ValidationFailure>();
        var json = ValidJson.Replace("12 Phố Test", new string('a', 201));

        UpdateShippingSettingsRequest.Read(Parse(json), failures);

        Assert.Equal(["line"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_UnknownField_ReportsThatField()
    {
        var failures = new List<ValidationFailure>();
        var json = ValidJson.Replace("}", ",\"provinceName\":\"Tự đặt\"}");

        UpdateShippingSettingsRequest.Read(Parse(json), failures);

        Assert.Equal(["provinceName"], failures.Select(failure => failure.PropertyName));
    }

    [Fact]
    public void Read_NullBody_ReportsBodyError()
    {
        var failures = new List<ValidationFailure>();

        UpdateShippingSettingsRequest.Read(null, failures);

        Assert.Single(failures);
    }

    [Fact]
    // Mỗi trường sai báo một lỗi riêng, để UI hiện lỗi dưới đúng ô.
    public void Read_SeveralInvalidFields_ReportsEachOnce()
    {
        var failures = new List<ValidationFailure>();
        var body = Parse("""{"senderName":"","senderPhone":"abc","provinceCode":"","wardCode":5,"line":""}""");

        UpdateShippingSettingsRequest.Read(body, failures);

        Assert.Equal(
            ["line", "provinceCode", "senderName", "senderPhone", "wardCode"],
            failures.Select(failure => failure.PropertyName).Order(StringComparer.Ordinal));
    }
}
