using System.Text.Json;
using Pickory.Modules.Catalog.Features.Products;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Products;

// Nối giá trị phân loại của biến thể (SKU) thành một ô chữ ở cột "Phân loại" của file xuất sản phẩm.
public sealed class ProductExportOptionTextTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    // Nhiều khóa nối bằng dấu phẩy và một khoảng trắng, theo thứ tự đọc từ JSON chứ không theo thứ tự chữ cái.
    public void Join_MultipleKeys_JoinsValuesInJsonOrder()
    {
        Assert.Equal("256 GB, Đen", ProductExportOptionText.Join(Json("""{"storage":"256 GB","color":"Đen"}""")));
    }

    [Fact]
    // Một khóa cho đúng một giá trị, không có dấu nối thừa.
    public void Join_SingleKey_ReturnsTheValueOnly()
    {
        Assert.Equal("Đen", ProductExportOptionText.Join(Json("""{"color":"Đen"}""")));
    }

    [Fact]
    // Giá trị số và logic giữ dạng JSON, nên số 8 thành "8" và true thành "true".
    public void Join_NumberAndBooleanValues_KeepJsonForm()
    {
        Assert.Equal("8, true, false", ProductExportOptionText.Join(Json("""{"ram":8,"nfc":true,"esim":false}""")));
    }

    [Fact]
    // Giá trị mảng hoặc object cũng giữ nguyên dạng JSON, để nhân viên không mất thông tin.
    public void Join_ArrayAndObjectValues_KeepJsonText()
    {
        Assert.Equal("[1,2], {\"a\":1}", ProductExportOptionText.Join(Json("""{"list":[1,2],"nested":{"a":1}}""")));
    }

    [Fact]
    // Chuỗi rỗng bị bỏ, nên không sinh dấu nối trống như ", Đen".
    public void Join_EmptyStringValue_IsSkipped()
    {
        Assert.Equal("Đen", ProductExportOptionText.Join(Json("""{"size":"","color":"Đen"}""")));
    }

    [Fact]
    // Giá trị toàn khoảng trắng bị bỏ như giá trị rỗng, nên không sinh chuỗi như "Đen,  ".
    public void Join_WhitespaceOnlyValue_IsSkipped()
    {
        Assert.Equal("Đen", ProductExportOptionText.Join(Json("""{"color":"Đen","size":"  "}""")));
    }

    [Fact]
    // Giá trị null bị bỏ như giá trị rỗng, vì chữ "null" không có nghĩa với người đọc file.
    public void Join_NullValue_IsSkipped()
    {
        Assert.Equal("Đen", ProductExportOptionText.Join(Json("""{"size":null,"color":"Đen"}""")));
    }

    [Fact]
    // Mọi giá trị đều rỗng thì ô để trống (null), không phải chuỗi rỗng.
    public void Join_AllValuesEmpty_ReturnsNull()
    {
        Assert.Null(ProductExportOptionText.Join(Json("""{"size":"","color":null}""")));
    }

    [Fact]
    // Sản phẩm không có phân loại lưu object rỗng. Ô để trống.
    public void Join_EmptyObject_ReturnsNull()
    {
        Assert.Null(ProductExportOptionText.Join(Json("{}")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"Đen\"]")]
    [InlineData("\"Đen\"")]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("null")]
    // Dữ liệu cũ hoặc hỏng không phải object. Ô để trống thay vì làm hỏng cả file xuất.
    public void Join_JsonThatIsNotObject_ReturnsNull(string json)
    {
        Assert.Null(ProductExportOptionText.Join(Json(json)));
    }
}
