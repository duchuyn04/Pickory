using System.Text.Json;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Orders.Admin;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

// Ghép chữ của file xuất đơn hàng: phân loại của dòng hàng và địa chỉ giao.
public sealed class OrderExportSheetsTests
{
    private static string? JoinVariant(string json)
    {
        using var document = JsonDocument.Parse(json);
        return OrderExportSheets.JoinVariant(document);
    }

    [Fact]
    // Nhiều khóa nối bằng `, ` theo thứ tự đọc từ JSON, đúng ví dụ `Đen, 256 GB` của spec.
    public void JoinVariant_MultipleKeys_JoinsValuesInReadOrder()
    {
        Assert.Equal("Đen, 256 GB", JoinVariant("""{"Màu":"Đen","Dung lượng":"256 GB"}"""));
    }

    [Fact]
    // Giá trị số và logic giữ dạng JSON, nên không mất thông tin phân loại.
    public void JoinVariant_NonStringValues_KeepJsonText()
    {
        Assert.Equal("1, true, x", JoinVariant("""{"a":1,"b":true,"c":"x"}"""));
    }

    [Fact]
    // Giá trị rỗng hoặc chỉ có khoảng trắng bị bỏ, để không có dấu phẩy thừa.
    public void JoinVariant_EmptyValues_AreSkipped()
    {
        Assert.Equal("x, y", JoinVariant("""{"a":"","b":"x","c":"   ","d":"y"}"""));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"a":"","b":" "}""")]
    // Object rỗng hoặc chỉ có giá trị rỗng thì không có phân loại, ô để trống.
    public void JoinVariant_NoUsableValue_ReturnsNull(string json)
    {
        Assert.Null(JoinVariant(json));
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"Đen\"")]
    [InlineData("null")]
    [InlineData("5")]
    // JSON không phải object là dữ liệu lỗi. Hàm trả `null` thay vì ném lỗi làm hỏng cả file.
    public void JoinVariant_NotAnObject_ReturnsNull(string json)
    {
        Assert.Null(JoinVariant(json));
    }

    [Theory]
    [InlineData("""{"Màu":"Đen","Dung lượng":"256 GB"}""")]
    [InlineData("""{"Size":"M"}""")]
    // Với giá trị chữ không rỗng, file xuất hiện cùng phân loại với trang chi tiết đơn.
    public void JoinVariant_StringValues_MatchesOrderDetailPage(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(OrderItemMapper.FormatVariantName(document), OrderExportSheets.JoinVariant(document));
    }

    [Fact]
    // Khác biệt có chủ đích: trang chi tiết bỏ cả phân loại khi có giá trị không phải chữ, file xuất giữ giá trị đó.
    public void JoinVariant_NonStringValue_DiffersFromOrderDetailPageOnPurpose()
    {
        using var document = JsonDocument.Parse("""{"a":1,"b":"x"}""");

        Assert.Null(OrderItemMapper.FormatVariantName(document));
        Assert.Equal("1, x", OrderExportSheets.JoinVariant(document));
    }

    [Fact]
    public void JoinShippingAddress_AllParts_JoinsFromSmallestToLargest()
    {
        Assert.Equal(
            "45 Lê Lợi, Phường Bến Nghé, TP. Hồ Chí Minh",
            OrderExportSheets.JoinShippingAddress("45 Lê Lợi", "Phường Bến Nghé", "TP. Hồ Chí Minh"));
    }

    [Theory]
    [InlineData("45 Lê Lợi", null, "Hà Nội", "45 Lê Lợi, Hà Nội")]
    [InlineData("45 Lê Lợi", "", "Hà Nội", "45 Lê Lợi, Hà Nội")]
    [InlineData(null, "Phường Ba Đình", "Hà Nội", "Phường Ba Đình, Hà Nội")]
    [InlineData("45 Lê Lợi", "  ", null, "45 Lê Lợi")]
    [InlineData(null, null, "Hà Nội", "Hà Nội")]
    // Phần thiếu, rỗng hoặc chỉ có khoảng trắng bị bỏ, nên không có dấu phẩy thừa ở đầu, giữa hay cuối.
    public void JoinShippingAddress_MissingParts_AreSkipped(string? line, string? ward, string? province, string expected)
    {
        Assert.Equal(expected, OrderExportSheets.JoinShippingAddress(line, ward, province));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", " ", "")]
    // Không có phần nào thì kết quả là chuỗi rỗng, không ném lỗi.
    public void JoinShippingAddress_NoParts_ReturnsEmptyString(string? line, string? ward, string? province)
    {
        Assert.Equal(string.Empty, OrderExportSheets.JoinShippingAddress(line, ward, province));
    }
}
