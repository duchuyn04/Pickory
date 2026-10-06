using System.Text.Json.Nodes;
using Pickory.Shared.Privacy;
using Xunit;

namespace Pickory.UnitTests.Shared.Privacy;

public sealed class PiiRedactorTests
{
    [Theory]
    [InlineData("0912 345 678")]
    [InlineData("+84 912345678")]
    [InlineData("091.234.5678")]
    [InlineData("a@b.vn")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("12345678901234")]
    [InlineData("STK: 12345678")]
    [InlineData("Số tài khoản 1234 5678 9012")]
    // Số điện thoại, email, số thẻ và số tài khoản không được rời hệ thống, giống bộ ví dụ của bản Node.
    public void Redact_HidesSensitiveValues(string value)
    {
        Assert.DoesNotContain(value, PiiRedactor.Redact($"Liên hệ {value}."));
        Assert.Contains("[đã ẩn]", PiiRedactor.Redact(value));
    }

    [Theory]
    [InlineData("25.990.000")]
    [InlineData("25990000đ")]
    [InlineData("5 triệu")]
    [InlineData("RTX 4070")]
    [InlineData("SM-S918B")]
    [InlineData("Giá 25 990 000 VND")]
    // Giá và mã sản phẩm phải giữ nguyên. Ẩn nhầm làm câu trả lời của AI mất thông tin bán hàng.
    public void Redact_KeepsPricesAndModelNames(string value)
    {
        Assert.Equal(value, PiiRedactor.Redact(value));
    }

    [Fact]
    // Ẩn nhiều giá trị trong một câu. Chạy lại trên kết quả đã ẩn không đổi gì thêm.
    public void Redact_HidesSeveralValuesAndIsIdempotent()
    {
        const string value = "Gọi 0912 345 678 hoặc a@b.vn, giá 25.990.000đ.";
        const string redacted = "Gọi [đã ẩn] hoặc [đã ẩn], giá 25.990.000đ.";

        Assert.Equal(redacted, PiiRedactor.Redact(value));
        Assert.Equal(redacted, PiiRedactor.Redact(redacted));
    }

    [Fact]
    // Mẫu chỉ nhận chữ số ASCII như regex của bản Node. Chuỗi trộn chữ số Ả Rập không phải số điện thoại.
    public void Redact_IgnoresNonAsciiDigits()
    {
        const string mixedDigits = "0912٣٤٥٦٧٨";

        Assert.Equal(mixedDigits, PiiRedactor.Redact(mixedDigits));
    }

    [Fact]
    // Ẩn theo cây JSON: chuỗi, khóa và số nhạy cảm đều được ẩn, cấu trúc JSON vẫn hợp lệ.
    public void RedactValue_RedactsStringsKeysAndSensitiveNumbers()
    {
        var value = JsonNode.Parse("""
            {
              "phone": "0912 345 678",
              "a@b.vn": "khóa là email",
              "card": 4111111111111111,
              "price": 25990000,
              "items": ["a@b.vn", 12, null]
            }
            """);

        var redacted = PiiRedactor.RedactValue(value)!;

        Assert.Equal("[đã ẩn]", redacted["phone"]!.GetValue<string>());
        Assert.Equal("khóa là email", redacted["[đã ẩn]"]!.GetValue<string>());
        Assert.Equal("[đã ẩn]", redacted["card"]!.GetValue<string>());
        Assert.Equal(25990000, redacted["price"]!.GetValue<long>());
        Assert.Equal("[đã ẩn]", redacted["items"]![0]!.GetValue<string>());
        Assert.Equal(12, redacted["items"]![1]!.GetValue<int>());
        Assert.Null(redacted["items"]![2]);
    }

    [Fact]
    // Giá trị gốc không bị sửa. Tool cục bộ vẫn nhận input gốc sau khi gateway ẩn bản gửi đi.
    public void RedactValue_DoesNotChangeTheOriginal()
    {
        var value = JsonNode.Parse("""{ "phone": "0912 345 678" }""");

        PiiRedactor.RedactValue(value);

        Assert.Equal("0912 345 678", value!["phone"]!.GetValue<string>());
    }
}
