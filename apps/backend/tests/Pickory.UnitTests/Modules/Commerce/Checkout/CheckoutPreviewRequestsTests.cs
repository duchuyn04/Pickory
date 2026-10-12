using System.Text.Json;
using FluentValidation;
using Pickory.Modules.Commerce.Checkout;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Checkout;

public sealed class CheckoutPreviewRequestsTests
{
    private const string ValidBody =
        """{"address":{"provinceCode":"1000001","wardCode":"1003646","line":"12 Phố Test"},"paymentMethod":"cod"}""";

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string[] FailedFields(string text)
    {
        var exception = Assert.Throws<ValidationException>(() => CheckoutPreviewRequests.Read(Json(text)));
        return exception.Errors.Select(failure => failure.PropertyName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void Read_ValidBody_ReturnsInput()
    {
        var input = CheckoutPreviewRequests.Read(Json(ValidBody));

        Assert.Equal(new CheckoutAddressInput("1000001", "1003646", "12 Phố Test"), input.Address);
        Assert.Equal("cod", input.PaymentMethod);
    }

    [Fact]
    public void Read_SepayMethod_IsAccepted()
    {
        var body = ValidBody.Replace("\"cod\"", "\"sepay\"");

        Assert.Equal("sepay", CheckoutPreviewRequests.Read(Json(body)).PaymentMethod);
    }

    [Fact]
    public void Read_LineWithSurroundingSpaces_IsTrimmed()
    {
        var body = ValidBody.Replace("12 Phố Test", "   12 Phố Test   ");

        Assert.Equal("12 Phố Test", CheckoutPreviewRequests.Read(Json(body)).Address.Line);
    }

    [Fact]
    public void Read_LineOfExactlyTwoHundredCharacters_IsAccepted()
    {
        var body = ValidBody.Replace("12 Phố Test", new string('a', 200));

        Assert.Equal(200, CheckoutPreviewRequests.Read(Json(body)).Address.Line.Length);
    }

    [Fact]
    public void Read_LineOfTwoHundredOneCharacters_FailsOnLine()
    {
        var body = ValidBody.Replace("12 Phố Test", new string('a', 201));

        Assert.Equal(["address.line"], FailedFields(body));
    }

    [Fact]
    public void Read_LineLongOnlyBecauseOfSpaces_IsAccepted()
    {
        var body = ValidBody.Replace("12 Phố Test", new string(' ', 150) + "12 Phố Test" + new string(' ', 150));

        Assert.Equal("12 Phố Test", CheckoutPreviewRequests.Read(Json(body)).Address.Line);
    }

    [Fact]
    public void Read_BlankLine_FailsOnLine()
    {
        var body = ValidBody.Replace("12 Phố Test", "   ");

        Assert.Equal(["address.line"], FailedFields(body));
    }

    [Fact]
    public void Read_MissingLine_FailsOnLine()
    {
        var body = """{"address":{"provinceCode":"1000001","wardCode":"1003646"},"paymentMethod":"cod"}""";

        Assert.Equal(["address.line"], FailedFields(body));
    }

    [Fact]
    public void Read_NumericProvinceCode_FailsOnProvinceCode()
    {
        var body = ValidBody.Replace("\"1000001\"", "1000001");

        Assert.Equal(["address.provinceCode"], FailedFields(body));
    }

    [Fact]
    public void Read_MissingWardCode_FailsOnWardCode()
    {
        var body = """{"address":{"provinceCode":"1000001","line":"12 Phố Test"},"paymentMethod":"cod"}""";

        Assert.Equal(["address.wardCode"], FailedFields(body));
    }

    [Fact]
    public void Read_MissingAddress_FailsOnAddress()
    {
        Assert.Equal(["address"], FailedFields("""{"paymentMethod":"cod"}"""));
    }

    [Fact]
    public void Read_AddressIsNotObject_FailsOnAddress()
    {
        Assert.Equal(["address"], FailedFields("""{"address":"Hà Nội","paymentMethod":"cod"}"""));
    }

    [Theory]
    [InlineData("""{"address":{"provinceCode":"1","wardCode":"2","line":"x"}}""")]
    [InlineData("""{"address":{"provinceCode":"1","wardCode":"2","line":"x"},"paymentMethod":null}""")]
    [InlineData("""{"address":{"provinceCode":"1","wardCode":"2","line":"x"},"paymentMethod":"COD"}""")]
    [InlineData("""{"address":{"provinceCode":"1","wardCode":"2","line":"x"},"paymentMethod":"momo"}""")]
    [InlineData("""{"address":{"provinceCode":"1","wardCode":"2","line":"x"},"paymentMethod":1}""")]
    public void Read_InvalidPaymentMethod_FailsOnPaymentMethod(string body)
    {
        Assert.Equal(["paymentMethod"], FailedFields(body));
    }

    [Fact]
    public void Read_UnknownTopLevelField_FailsOnThatField()
    {
        var body = ValidBody.Replace("\"paymentMethod\"", "\"items\":[],\"paymentMethod\"");

        Assert.Equal(["items"], FailedFields(body));
    }

    [Fact]
    public void Read_UnknownAddressField_FailsOnThatField()
    {
        var body = ValidBody.Replace("\"line\"", "\"provinceName\":\"Giả\",\"line\"");

        Assert.Equal(["address.provinceName"], FailedFields(body));
    }

    [Fact]
    public void Read_SeveralProblems_ReportsEachFieldOnce()
    {
        var body = """{"address":{"provinceCode":"","wardCode":"2","line":"x"},"paymentMethod":"momo"}""";

        Assert.Equal(["address.provinceCode", "paymentMethod"], FailedFields(body));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public void Read_BodyIsNotObject_FailsOnWholeBody(string body)
    {
        Assert.Equal([string.Empty], FailedFields(body));
    }

    [Fact]
    public void Read_MissingBody_FailsOnWholeBody()
    {
        var exception = Assert.Throws<ValidationException>(() => CheckoutPreviewRequests.Read(null));

        Assert.Equal([string.Empty], exception.Errors.Select(failure => failure.PropertyName).ToArray());
    }
}
