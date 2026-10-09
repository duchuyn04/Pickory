using FluentValidation;
using Pickory.Modules.Catalog.Features.Storefront;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Storefront;

// CAT-09: đọc và kiểm tham số của `GET /api/products`. Logic thuần, không cần DB hay HTTP.
public sealed class ProductSearchQueryTests
{
    private static Dictionary<string, string> Parameters(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value);

    private static ValidationException AssertInvalid(params (string Name, string Value)[] pairs)
    {
        return Assert.Throws<ValidationException>(() => ProductSearchQuery.Read(Parameters(pairs)));
    }

    [Fact]
    public void Read_WithoutParameters_UsesDefaults()
    {
        var query = ProductSearchQuery.Read(Parameters());

        Assert.Null(query.CategorySlug);
        Assert.Empty(query.BrandSlugs);
        Assert.Null(query.PriceMin);
        Assert.Null(query.PriceMax);
        Assert.False(query.InStockOnly);
        Assert.Empty(query.AttributeParameters);
        Assert.Equal(ProductSort.Newest, query.Sort);
        Assert.Equal(1, query.Page);
        Assert.Equal(24, query.PageSize);
    }

    [Fact]
    public void Read_WithEveryParameter_ReadsEachValue()
    {
        var query = ProductSearchQuery.Read(Parameters(
            ("category", "laptop-gaming"), ("brand", "asus,msi"), ("priceMin", "100"), ("priceMax", "900"),
            ("inStock", "true"), ("sort", "price_desc"), ("page", "3"), ("pageSize", "10"),
            ("attr.ram_gb", "16,32")));

        Assert.Equal("laptop-gaming", query.CategorySlug);
        Assert.Equal(["asus", "msi"], query.BrandSlugs);
        Assert.Equal(100, query.PriceMin);
        Assert.Equal(900, query.PriceMax);
        Assert.True(query.InStockOnly);
        Assert.Equal(ProductSort.PriceDescending, query.Sort);
        Assert.Equal(3, query.Page);
        Assert.Equal(10, query.PageSize);
        Assert.Equal("16,32", query.AttributeParameters["attr.ram_gb"]);
    }

    [Theory]
    [InlineData("newest", ProductSort.Newest)]
    [InlineData("price_asc", ProductSort.PriceAscending)]
    [InlineData("price_desc", ProductSort.PriceDescending)]
    public void Read_ReadsEverySortKind(string raw, ProductSort expected)
    {
        Assert.Equal(expected, ProductSearchQuery.Read(Parameters(("sort", raw))).Sort);
    }

    [Theory]
    [InlineData("cheapest")]
    [InlineData("PRICE_ASC")]
    [InlineData("")]
    public void Read_UnknownSort_IsInvalid(string raw)
    {
        var error = AssertInvalid(("sort", raw));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "sort");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("24")]
    [InlineData("48")]
    public void Read_PageSizeInsideRange_IsAccepted(string raw)
    {
        Assert.Equal(int.Parse(raw), ProductSearchQuery.Read(Parameters(("pageSize", raw))).PageSize);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("49")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Read_PageSizeOutsideRange_IsInvalid(string raw)
    {
        var error = AssertInvalid(("pageSize", raw));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "pageSize");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("x")]
    public void Read_PageBelowOne_IsInvalid(string raw)
    {
        var error = AssertInvalid(("page", raw));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "page");
    }

    [Fact]
    public void Read_PageWhoseOffsetOverflowsInteger_IsInvalid()
    {
        // Độ lệch (page - 1) * pageSize vượt int.MaxValue sẽ thành số âm và làm DB báo lỗi 500.
        var error = AssertInvalid(("page", int.MaxValue.ToString()), ("pageSize", "48"));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "page");
    }

    [Fact]
    public void Read_LastPageWhoseOffsetFitsInteger_IsAccepted()
    {
        var query = ProductSearchQuery.Read(Parameters(("page", "1000000"), ("pageSize", "48")));
        Assert.Equal(47_999_952, query.Skip);
    }

    [Fact]
    public void Read_PriceMinGreaterThanPriceMax_IsInvalid()
    {
        var error = AssertInvalid(("priceMin", "500"), ("priceMax", "100"));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "priceMin");
    }

    [Fact]
    public void Read_EqualPriceBounds_AreAccepted()
    {
        var query = ProductSearchQuery.Read(Parameters(("priceMin", "500"), ("priceMax", "500")));
        Assert.Equal(500, query.PriceMin);
        Assert.Equal(500, query.PriceMax);
    }

    [Theory]
    [InlineData("priceMin", "-1")]
    [InlineData("priceMin", "1.5")]
    [InlineData("priceMax", "abc")]
    [InlineData("priceMax", "99999999999")]
    public void Read_PriceThatIsNotNonNegativeInteger_IsInvalid(string name, string raw)
    {
        var error = AssertInvalid((name, raw));
        Assert.Contains(error.Errors, failure => failure.PropertyName == name);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Read_InStock_AcceptsOnlyTrueOrFalse(string raw, bool expected)
    {
        Assert.Equal(expected, ProductSearchQuery.Read(Parameters(("inStock", raw))).InStockOnly);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("TRUE")]
    [InlineData("")]
    public void Read_InStockWithOtherValue_IsInvalid(string raw)
    {
        var error = AssertInvalid(("inStock", raw));
        Assert.Contains(error.Errors, failure => failure.PropertyName == "inStock");
    }

    [Fact]
    public void Read_BrandList_TrimsSpacesAndDropsEmptyItems()
    {
        var query = ProductSearchQuery.Read(Parameters(("brand", " asus , ,msi,")));
        Assert.Equal(["asus", "msi"], query.BrandSlugs);
    }

    [Fact]
    public void Read_CollectsOnlyAttributeParameters()
    {
        var query = ProductSearchQuery.Read(Parameters(("attr.ram_gb", "16"), ("attr.screen_inch_min", "15"), ("other", "x")));
        Assert.Equal(["attr.ram_gb", "attr.screen_inch_min"], query.AttributeParameters.Keys.Order());
    }

    [Fact]
    public void Read_ReportsEveryInvalidParameterInOneError()
    {
        var error = AssertInvalid(("page", "0"), ("pageSize", "49"), ("sort", "x"));
        Assert.Equal(["page", "pageSize", "sort"], error.Errors.Select(failure => failure.PropertyName).Order());
    }
}
