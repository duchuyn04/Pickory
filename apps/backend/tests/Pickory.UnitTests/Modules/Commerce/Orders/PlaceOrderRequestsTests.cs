using System.Text.Json;
using FluentValidation;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class PlaceOrderRequestsTests
{
    private const string ValidBody = """
        {
          "contact": { "name": "Nguyễn Văn An", "phone": "0901234567", "email": "an@example.com" },
          "address": { "provinceCode": "1000001", "wardCode": "1003646", "line": "12 Phố Test" },
          "note": "Giao giờ hành chính",
          "paymentMethod": "cod",
          "expectedTotal": 123000
        }
        """;

    private const string InvoiceJson = """
        "invoice": { "companyName": "Công ty A", "taxCode": "0312345678-001", "address": "1 Đường B", "email": "ketoan@example.com" }
        """;

    private const string AddressAndPaymentJson =
        "\"address\":{\"provinceCode\":\"1\",\"wardCode\":\"2\",\"line\":\"x\"},\"paymentMethod\":\"cod\",\"expectedTotal\":1";

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    // Thêm một trường vào cuối đối tượng JSON gốc của body mẫu.
    private static string WithExtra(string body, string extraJson) => body.TrimEnd().TrimEnd('}') + "," + extraJson + "}";

    private static string[] FailedFields(string text)
    {
        var exception = Assert.Throws<ValidationException>(() => PlaceOrderRequests.Read(Json(text)));
        return exception.Errors.Select(failure => failure.PropertyName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void Read_ValidBody_ReturnsInput()
    {
        var input = PlaceOrderRequests.Read(Json(ValidBody));

        Assert.Equal(new OrderContactInput("Nguyễn Văn An", "0901234567", "an@example.com"), input.Contact);
        Assert.Equal(new CheckoutAddressInput("1000001", "1003646", "12 Phố Test"), input.Address);
        Assert.Equal("Giao giờ hành chính", input.Note);
        Assert.Equal("cod", input.PaymentMethod);
        Assert.Null(input.Invoice);
        Assert.Equal(123000, input.ExpectedTotal);
    }

    [Fact]
    public void Read_PhoneInInternationalForm_IsStoredNormalized()
    {
        var body = ValidBody.Replace("0901234567", "+84 901 234 567");

        Assert.Equal("0901234567", PlaceOrderRequests.Read(Json(body)).Contact.Phone);
    }

    [Theory]
    [InlineData("subtotal", "1")]
    [InlineData("shippingFee", "1")]
    [InlineData("shippingDiscount", "1")]
    [InlineData("total", "1")]
    [InlineData("items", """[{"skuId":"x","unitPrice":1,"quantity":1}]""")]
    public void Read_MoneyFieldsFromClient_AreIgnored(string field, string valueJson)
    {
        var body = WithExtra(ValidBody, $"\"{field}\": {valueJson}");

        var input = PlaceOrderRequests.Read(Json(body));

        Assert.Equal(123000, input.ExpectedTotal);
    }

    [Theory]
    [InlineData("""{"contact":{"phone":"0901234567","email":"an@example.com"}""", "contact.name")]
    [InlineData("""{"contact":{"name":"   ","phone":"0901234567","email":"an@example.com"}""", "contact.name")]
    [InlineData("""{"contact":{"name":"An","email":"an@example.com"}""", "contact.phone")]
    [InlineData("""{"contact":{"name":"An","phone":"12345","email":"an@example.com"}""", "contact.phone")]
    [InlineData("""{"contact":{"name":"An","phone":"0901234567"}""", "contact.email")]
    [InlineData("""{"contact":{"name":"An","phone":"0901234567","email":"khong-phai-email"}""", "contact.email")]
    public void Read_InvalidContactField_FailsOnThatField(string contactFragment, string expectedField)
    {
        var body = contactFragment + "," + AddressAndPaymentJson + "}";

        Assert.Equal([expectedField], FailedFields(body));
    }

    [Fact]
    public void Read_MissingContact_FailsOnContact()
    {
        var body = "{" + AddressAndPaymentJson + "}";

        Assert.Equal(["contact"], FailedFields(body));
    }

    [Fact]
    public void Read_AddressWithBlankLine_FailsOnAddressLine()
    {
        var body = ValidBody.Replace("\"line\": \"12 Phố Test\"", "\"line\": \"\"");

        Assert.Equal(["address.line"], FailedFields(body));
    }

    [Fact]
    public void Read_NoteOfFiveHundredCharacters_IsAccepted()
    {
        var body = ValidBody.Replace("Giao giờ hành chính", new string('a', 500));

        Assert.Equal(500, PlaceOrderRequests.Read(Json(body)).Note!.Length);
    }

    [Fact]
    public void Read_NoteOfFiveHundredOneCharacters_FailsOnNote()
    {
        var body = ValidBody.Replace("Giao giờ hành chính", new string('a', 501));

        Assert.Equal(["note"], FailedFields(body));
    }

    [Fact]
    public void Read_BlankNote_BecomesNull()
    {
        var body = ValidBody.Replace("Giao giờ hành chính", "   ");

        Assert.Null(PlaceOrderRequests.Read(Json(body)).Note);
    }

    [Fact]
    public void Read_MissingNote_BecomesNull()
    {
        var body = ValidBody.Replace("\"note\": \"Giao giờ hành chính\",", string.Empty);

        Assert.Null(PlaceOrderRequests.Read(Json(body)).Note);
    }

    [Fact]
    public void Read_NoteIsNotString_FailsOnNote()
    {
        var body = ValidBody.Replace("\"Giao giờ hành chính\"", "123");

        Assert.Equal(["note"], FailedFields(body));
    }

    [Fact]
    public void Read_InvalidPaymentMethod_FailsOnPaymentMethod()
    {
        var body = ValidBody.Replace("\"cod\"", "\"momo\"");

        Assert.Equal(["paymentMethod"], FailedFields(body));
    }

    [Fact]
    public void Read_ValidInvoice_ReturnsInvoice()
    {
        var input = PlaceOrderRequests.Read(Json(WithExtra(ValidBody, InvoiceJson)));

        Assert.Equal(
            new OrderInvoiceInput("Công ty A", "0312345678-001", "1 Đường B", "ketoan@example.com"), input.Invoice);
    }

    [Fact]
    public void Read_InvoiceNull_MeansNoInvoice()
    {
        var input = PlaceOrderRequests.Read(Json(WithExtra(ValidBody, "\"invoice\": null")));

        Assert.Null(input.Invoice);
    }

    [Theory]
    [InlineData("companyName")]
    [InlineData("taxCode")]
    [InlineData("address")]
    [InlineData("email")]
    public void Read_InvoiceMissingField_FailsOnThatField(string field)
    {
        var invoice = new Dictionary<string, string>
        {
            ["companyName"] = "Công ty A",
            ["taxCode"] = "0312345678",
            ["address"] = "1 Đường B",
            ["email"] = "ketoan@example.com",
        };
        invoice.Remove(field);
        var invoiceJson = "\"invoice\":" + JsonSerializer.Serialize(invoice);

        Assert.Equal(["invoice." + field], FailedFields(WithExtra(ValidBody, invoiceJson)));
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("03123456AB")]
    public void Read_InvoiceWithBadTaxCode_FailsOnTaxCode(string taxCode)
    {
        var body = WithExtra(ValidBody, InvoiceJson).Replace("0312345678-001", taxCode);

        Assert.Equal(["invoice.taxCode"], FailedFields(body));
    }

    [Fact]
    public void Read_InvoiceWithBadEmail_FailsOnInvoiceEmail()
    {
        var body = WithExtra(ValidBody, InvoiceJson).Replace("ketoan@example.com", "khong-phai-email");

        Assert.Equal(["invoice.email"], FailedFields(body));
    }

    [Fact]
    public void Read_InvoiceIsNotObject_FailsOnInvoice()
    {
        Assert.Equal(["invoice"], FailedFields(WithExtra(ValidBody, "\"invoice\": \"abc\"")));
    }

    [Theory]
    [InlineData("\"123000\"")]
    [InlineData("123000.5")]
    [InlineData("-1")]
    [InlineData("null")]
    public void Read_InvalidExpectedTotal_FailsOnExpectedTotal(string valueJson)
    {
        var body = ValidBody.Replace("123000", valueJson);

        Assert.Equal(["expectedTotal"], FailedFields(body));
    }

    [Fact]
    public void Read_MissingExpectedTotal_FailsOnExpectedTotal()
    {
        var body = ValidBody.Replace(",\n  \"expectedTotal\": 123000", string.Empty)
            .Replace(",\r\n  \"expectedTotal\": 123000", string.Empty);
        Assert.DoesNotContain("expectedTotal", body);

        Assert.Equal(["expectedTotal"], FailedFields(body));
    }

    [Fact]
    public void Read_ManyErrors_ReportsEveryField()
    {
        var body = """{"contact":{},"address":{"provinceCode":"1","wardCode":"2","line":"x"},"paymentMethod":"cod"}""";

        Assert.Equal(["contact.email", "contact.name", "contact.phone", "expectedTotal"], FailedFields(body));
    }

    [Fact]
    public void Read_BodyIsNotObject_FailsOnWholeBody()
    {
        Assert.Equal([string.Empty], FailedFields("[]"));
    }
}
