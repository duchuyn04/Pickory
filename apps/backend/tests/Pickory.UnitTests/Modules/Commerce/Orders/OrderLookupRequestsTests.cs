using System.Text.Json;
using FluentValidation;
using Pickory.Modules.Commerce.Orders.Lookup;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

// Kiểm tra luật chuẩn hóa và kiểm định dạng cho chức năng tra cứu đơn hàng.
public sealed class OrderLookupRequestsTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string[] FailedFields(string text)
    {
        var exception = Assert.Throws<ValidationException>(() => OrderLookupRequests.Read(Json(text)));
        return exception.Errors.Select(failure => failure.PropertyName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    // Mã đơn cần cắt khoảng trắng hai đầu và chuyển sang chữ hoa để khách gõ linh hoạt.
    public void NormalizeOrderCode_TrimsWhitespaceAndConvertsToUppercase()
    {
        Assert.Equal("PK23456789", OrderLookupNormalizer.NormalizeOrderCode("  pk23456789  "));
    }

    [Fact]
    // Mã đơn rỗng hoặc null chuyển thành chuỗi rỗng để tránh lỗi null reference.
    public void NormalizeOrderCode_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, OrderLookupNormalizer.NormalizeOrderCode(null));
        Assert.Equal(string.Empty, OrderLookupNormalizer.NormalizeOrderCode("   "));
    }

    [Fact]
    // Email cần cắt khoảng trắng hai đầu và chuyển sang chữ thường để đồng nhất với hệ thống.
    public void NormalizeEmail_TrimsWhitespaceAndConvertsToLowercase()
    {
        Assert.Equal("khach@example.com", OrderLookupNormalizer.NormalizeEmail("  Khach@Example.COM  "));
    }

    [Fact]
    // Email rỗng hoặc null chuyển thành chuỗi rỗng để tránh lỗi null reference.
    public void NormalizeEmail_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, OrderLookupNormalizer.NormalizeEmail(null));
        Assert.Equal(string.Empty, OrderLookupNormalizer.NormalizeEmail("   "));
    }

    [Fact]
    // Dữ liệu hợp lệ trả đối tượng đã chuẩn hóa mã đơn và email.
    public void Read_ValidBody_ReturnsNormalizedInput()
    {
        var body = """{ "orderCode": "  pk23456789  ", "email": "  Khach@Example.Com  " }""";

        var input = OrderLookupRequests.Read(Json(body));

        Assert.Equal("PK23456789", input.OrderCode);
        Assert.Equal("khach@example.com", input.Email);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"string\"")]
    [InlineData("123")]
    // Body không phải đối tượng JSON ném lỗi với trường trống để báo lỗi toàn bộ body.
    public void Read_BodyNotObject_FailsOnEmptyProperty(string json)
    {
        var element = json == "null" ? (JsonElement?)null : Json(json);

        var exception = Assert.Throws<ValidationException>(() => OrderLookupRequests.Read(element));

        var error = Assert.Single(exception.Errors);
        Assert.Equal(string.Empty, error.PropertyName);
    }

    [Theory]
    [InlineData("""{ "email": "khach@example.com" }""")]
    [InlineData("""{ "orderCode": null, "email": "khach@example.com" }""")]
    [InlineData("""{ "orderCode": "", "email": "khach@example.com" }""")]
    [InlineData("""{ "orderCode": "   ", "email": "khach@example.com" }""")]
    // Mã đơn thiếu hoặc rỗng bị từ chối để tránh tra cứu không xác định.
    public void Read_MissingOrEmptyOrderCode_FailsOnOrderCode(string body)
    {
        Assert.Equal([OrderLookupRequests.OrderCodeField], FailedFields(body));
    }

    [Theory]
    [InlineData("AB23456789")]
    [InlineData("pk234567")]
    [InlineData("PK234567890")]
    [InlineData("PK2345678I")]
    [InlineData("PK2345678O")]
    [InlineData("PK23456780")]
    [InlineData("PK23456781")]
    [InlineData("PK2345678@")]
    // Mã đơn sai tiền tố, sai độ dài hoặc chứa ký tự ngoài bảng chữ hợp lệ bị từ chối.
    public void Read_InvalidOrderCodeFormat_FailsOnOrderCode(string code)
    {
        var body = $$"""{ "orderCode": "{{code}}", "email": "khach@example.com" }""";

        Assert.Equal([OrderLookupRequests.OrderCodeField], FailedFields(body));
    }

    [Theory]
    [InlineData("""{ "orderCode": "PK23456789" }""")]
    [InlineData("""{ "orderCode": "PK23456789", "email": null }""")]
    [InlineData("""{ "orderCode": "PK23456789", "email": "" }""")]
    [InlineData("""{ "orderCode": "PK23456789", "email": "   " }""")]
    // Email thiếu hoặc rỗng bị từ chối để tránh không có địa chỉ nhận link xem đơn.
    public void Read_MissingOrEmptyEmail_FailsOnEmail(string body)
    {
        Assert.Equal([OrderLookupRequests.EmailField], FailedFields(body));
    }

    [Theory]
    [InlineData("khong-phai-email")]
    [InlineData("@example.com")]
    [InlineData("khach@")]
    [InlineData("khach@.com")]
    // Email sai định dạng theo chuẩn RFC bị từ chối để tránh gửi thư thất bại.
    public void Read_InvalidEmailFormat_FailsOnEmail(string email)
    {
        var body = $$"""{ "orderCode": "PK23456789", "email": "{{email}}" }""";

        Assert.Equal([OrderLookupRequests.EmailField], FailedFields(body));
    }

    [Fact]
    // Email dài quá 254 ký tự theo RFC 5321 bị từ chối.
    public void Read_EmailExceedsMaxLength_FailsOnEmail()
    {
        var longEmail = new string('a', 243) + "@example.com";
        var body = $$"""{ "orderCode": "PK23456789", "email": "{{longEmail}}" }""";

        Assert.Equal([OrderLookupRequests.EmailField], FailedFields(body));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{ "orderCode": "sai-ma", "email": "sai-email" }""")]
    // Cả hai trường đều thiếu hoặc sai thì phản hồi gom đủ lỗi của cả hai trường.
    public void Read_BothFieldsInvalid_FailsOnBothFields(string body)
    {
        Assert.Equal([OrderLookupRequests.EmailField, OrderLookupRequests.OrderCodeField], FailedFields(body));
    }
}
