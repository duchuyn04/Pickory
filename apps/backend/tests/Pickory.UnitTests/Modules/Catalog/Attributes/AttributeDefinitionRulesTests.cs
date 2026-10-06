using Pickory.Modules.Catalog.Attributes;
using Pickory.Shared.Exceptions;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Attributes;

// Port từ `attributes.test.ts` của bản Node (CAT-01).
public sealed class AttributeDefinitionRulesTests
{
    private static readonly AttributeDefinition RamAttribute = new()
    {
        Key = "ram_gb",
        Label = "Dung lượng RAM",
        Group = "Bộ nhớ",
        Type = AttributeTypes.Number,
        Unit = "GB",
        Required = true,
        Filterable = true,
    };

    private static readonly AttributeDefinition ColorAttribute = new()
    {
        Key = "color",
        Label = "Màu sắc",
        Group = "Thiết kế",
        Type = AttributeTypes.Enum,
        Options = ["Đen", "Bạc", "Xanh"],
        Required = false,
        Filterable = true,
    };

    [Fact]
    // Định nghĩa đầy đủ và đúng mẫu được nhận.
    public void Validate_AcceptsValidDefinition()
    {
        var result = AttributeDefinitionRules.Validate(RamAttribute);

        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("Ram_gb")]
    [InlineData("1ram")]
    [InlineData("ram-gb")]
    [InlineData("ram gb")]
    [InlineData("")]
    [InlineData("_ram")]
    // Khóa phải theo mẫu `^[a-z][a-z0-9_]*$`. Khóa sai làm hỏng query JSON và bộ lọc.
    public void Validate_RejectsMalformedKey(string key)
    {
        var result = AttributeDefinitionRules.Validate(RamAttribute with { Key = key });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, error => error.Key == key);
    }

    [Fact]
    // Kiểu `enum` thiếu `options` hoặc có danh sách rỗng bị từ chối.
    public void Validate_RejectsEnumWithoutOptions()
    {
        var enumWithoutOptions = new AttributeDefinition
        {
            Key = "screen_type",
            Label = "Loại màn hình",
            Group = "Màn hình",
            Type = AttributeTypes.Enum,
            Required = true,
            Filterable = true,
        };

        var withoutOptions = AttributeDefinitionRules.Validate(enumWithoutOptions);
        var emptyOptions = AttributeDefinitionRules.Validate(enumWithoutOptions with { Options = [] });

        Assert.False(withoutOptions.Valid);
        Assert.Equal("screen_type", withoutOptions.Errors[0].Key);
        Assert.False(emptyOptions.Valid);
        Assert.Equal("screen_type", emptyOptions.Errors[0].Key);
    }

    [Fact]
    // Kiểu khác `enum` không cần `options`.
    public void Validate_AcceptsNonEnumWithoutOptions()
    {
        var textAttribute = new AttributeDefinition
        {
            Key = "cpu_model",
            Label = "Vi xử lý",
            Group = "Hiệu năng",
            Type = AttributeTypes.Text,
            Required = true,
            Filterable = false,
        };

        Assert.True(AttributeDefinitionRules.Validate(textAttribute).Valid);
    }

    [Fact]
    // Kiểu ngoài 4 kiểu cho phép bị từ chối. Validate thông số không biết cách kiểm tra kiểu lạ.
    public void Validate_RejectsUnknownType()
    {
        var result = AttributeDefinitionRules.Validate(RamAttribute with { Type = "date" });

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, error => error.Key == "ram_gb");
    }

    [Fact]
    // Lựa chọn rỗng sau khi cắt khoảng trắng, hoặc lựa chọn trùng nhau, đều bị từ chối.
    public void Validate_RejectsBlankOrDuplicateOptions()
    {
        var blank = AttributeDefinitionRules.Validate(ColorAttribute with { Options = ["Đen", "   ", "Bạc"] });
        var duplicate = AttributeDefinitionRules.Validate(ColorAttribute with { Options = ["Đen", "Bạc", " Đen "] });

        Assert.False(blank.Valid);
        Assert.Contains(blank.Errors, error => error.Key == "color");
        Assert.False(duplicate.Valid);
        Assert.Contains(duplicate.Errors, error => error.Key == "color");
    }

    [Fact]
    // Nhãn và nhóm rỗng bị từ chối. Giao diện cần tên hiển thị và nhóm để chia bảng thông số.
    public void Validate_RejectsBlankLabelAndGroup()
    {
        var result = AttributeDefinitionRules.Validate(RamAttribute with { Label = " ", Group = "" });

        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    // Hai thuộc tính trùng khóa trong một danh sách bị từ chối.
    public void ValidateAll_RejectsDuplicateKeys()
    {
        var result = AttributeDefinitionRules.ValidateAll([RamAttribute, RamAttribute with { Label = "RAM khác" }]);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, error => error.Key == "ram_gb");
    }

    [Fact]
    // Danh mục con có thuộc tính của cha đứng trước, rồi tới thuộc tính của chính nó.
    public void Resolve_PutsParentAttributesFirst()
    {
        var resolved = CategoryAttributes.Resolve([RamAttribute], [ColorAttribute]);

        Assert.Equal(["ram_gb", "color"], resolved.Select(attribute => attribute.Key));
    }

    [Fact]
    // Danh mục gốc không có cha thì chỉ có thuộc tính của chính nó.
    public void Resolve_ReturnsChildAttributesWithoutParent()
    {
        Assert.Equal([ColorAttribute], CategoryAttributes.Resolve(null, [ColorAttribute]));
        Assert.Equal([ColorAttribute], CategoryAttributes.Resolve([], [ColorAttribute]));
    }

    [Fact]
    // Con trùng khóa với cha là lỗi định nghĩa. Con không được ghi đè thuộc tính kế thừa.
    public void Resolve_RejectsKeyShadowing()
    {
        var error = Assert.Throws<AppException>(
            () => CategoryAttributes.Resolve([RamAttribute], [RamAttribute with { Label = "RAM con" }]));

        Assert.Contains("Trùng khóa", error.Message);
    }
}
