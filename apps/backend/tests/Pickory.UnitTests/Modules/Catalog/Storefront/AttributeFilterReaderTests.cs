using FluentValidation;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Features.Storefront;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Storefront;

// CAT-09: đổi tham số `attr.*` thành bộ lọc theo Decision 11 của kế hoạch catalog.
public sealed class AttributeFilterReaderTests
{
    private static readonly IReadOnlyList<AttributeDefinition> FilterableAttributes =
    [
        new() { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = AttributeTypes.Enum, Options = ["8", "16", "32"], Filterable = true },
        new() { Key = "screen_inch", Label = "Màn hình", Group = "Màn hình", Type = AttributeTypes.Number, Unit = "inch", Filterable = true },
        new() { Key = "touch", Label = "Cảm ứng", Group = "Màn hình", Type = AttributeTypes.Boolean, Filterable = true },
        new() { Key = "cpu", Label = "CPU", Group = "Cấu hình", Type = AttributeTypes.Text, Filterable = true },
    ];

    private static IReadOnlyList<AttributeFilter> Read(params (string Name, string Value)[] pairs) =>
        AttributeFilterReader.Read(pairs.ToDictionary(pair => pair.Name, pair => pair.Value), FilterableAttributes);

    [Fact]
    public void Read_EnumValueList_SplitsOnCommaAndTrimsSpaces()
    {
        var filter = Assert.IsType<ValueListFilter>(Assert.Single(Read(("attr.ram_gb", " 16 , 32 ,"))));
        Assert.Equal("ram_gb", filter.Key);
        Assert.Equal(["16", "32"], filter.Values);
    }

    [Fact]
    public void Read_TextValueList_IsAccepted()
    {
        var filter = Assert.IsType<ValueListFilter>(Assert.Single(Read(("attr.cpu", "i7,i9"))));
        Assert.Equal(["i7", "i9"], filter.Values);
    }

    [Fact]
    public void Read_NumberMinAndMax_BuildOneClosedRange()
    {
        var filter = Assert.IsType<NumberRangeFilter>(Assert.Single(Read(("attr.screen_inch_min", "13.5"), ("attr.screen_inch_max", "16"))));
        Assert.Equal("screen_inch", filter.Key);
        Assert.Equal(13.5m, filter.Min);
        Assert.Equal(16m, filter.Max);
    }

    [Fact]
    public void Read_NumberWithOnlyMin_LeavesMaxOpen()
    {
        var filter = Assert.IsType<NumberRangeFilter>(Assert.Single(Read(("attr.screen_inch_min", "15"))));
        Assert.Equal(15m, filter.Min);
        Assert.Null(filter.Max);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Read_Boolean_AcceptsTrueAndFalse(string raw, bool expected)
    {
        var filter = Assert.IsType<BooleanFilter>(Assert.Single(Read(("attr.touch", raw))));
        Assert.Equal(expected, filter.Value);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("TRUE")]
    public void Read_BooleanWithOtherValue_IsInvalid(string raw)
    {
        var error = Assert.Throws<ValidationException>(() => Read(("attr.touch", raw)));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "attr.touch");
    }

    [Theory]
    [InlineData("attr.screen_inch_min", "abc")]
    [InlineData("attr.screen_inch_max", "")]
    public void Read_NumberThatIsNotNumeric_IsInvalid(string name, string raw)
    {
        var error = Assert.Throws<ValidationException>(() => Read((name, raw)));
        Assert.Contains(error.Errors, failure => failure.PropertyName == name);
    }

    [Fact]
    public void Read_NumberMinGreaterThanMax_IsInvalid()
    {
        var error = Assert.Throws<ValidationException>(() => Read(("attr.screen_inch_min", "17"), ("attr.screen_inch_max", "15")));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "attr.screen_inch_min");
    }

    [Fact]
    public void Read_NumberWithoutMinOrMaxSuffix_IsInvalid()
    {
        var error = Assert.Throws<ValidationException>(() => Read(("attr.screen_inch", "15")));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "attr.screen_inch");
    }

    [Fact]
    public void Read_EnumWithMinSuffix_IsUnknownFilter()
    {
        var error = Assert.Throws<UnknownFilterException>(() => Read(("attr.ram_gb_min", "8")));
        Assert.Equal(["ram_gb_min"], error.Keys);
    }

    [Fact]
    public void Read_EmptyValueList_IsInvalid()
    {
        var error = Assert.Throws<ValidationException>(() => Read(("attr.ram_gb", " , ")));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "attr.ram_gb");
    }

    [Fact]
    public void Read_UnknownKey_ThrowsUnknownFilterWithKeyName()
    {
        var error = Assert.Throws<UnknownFilterException>(() => Read(("attr.gpu", "rtx")));
        Assert.Equal("UNKNOWN_FILTER", error.Code);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(["gpu"], error.Keys);
    }

    [Fact]
    public void Read_NonFilterableKey_ThrowsUnknownFilterWithKeyName()
    {
        // `weight_kg` có trong danh mục nhưng không `filterable`, nên danh sách lọc được không chứa nó.
        var error = Assert.Throws<UnknownFilterException>(() => Read(("attr.weight_kg_max", "2")));
        Assert.Equal(["weight_kg_max"], error.Keys);
    }

    [Fact]
    public void Read_ListsEveryUnknownKeyInOrder()
    {
        var error = Assert.Throws<UnknownFilterException>(() => Read(("attr.zzz", "1"), ("attr.ram_gb", "8"), ("attr.aaa", "1")));
        Assert.Equal(["aaa", "zzz"], error.Keys);
    }

    [Fact]
    public void Read_WithoutCategoryAttributes_RejectsEveryAttrParameter()
    {
        // Không có `category` thì tập thuộc tính lọc được rỗng, nên mọi `attr.*` là khóa lạ.
        var error = Assert.Throws<UnknownFilterException>(() =>
            AttributeFilterReader.Read(new Dictionary<string, string> { ["attr.ram_gb"] = "16" }, []));
        Assert.Equal(["ram_gb"], error.Keys);
    }

    [Fact]
    public void Read_WithoutAttrParameters_ReturnsNoFilter()
    {
        Assert.Empty(AttributeFilterReader.Read(new Dictionary<string, string>(), FilterableAttributes));
    }

    [Fact]
    public void Read_KeyEndingWithMin_PrefersExactKeyMatch()
    {
        IReadOnlyList<AttributeDefinition> attributes =
        [
            new() { Key = "price_min", Label = "Giá nhỏ nhất", Group = "Giá", Type = AttributeTypes.Text, Filterable = true },
        ];
        var filters = AttributeFilterReader.Read(new Dictionary<string, string> { ["attr.price_min"] = "a" }, attributes);
        Assert.Equal("price_min", Assert.IsType<ValueListFilter>(Assert.Single(filters)).Key);
    }
}
