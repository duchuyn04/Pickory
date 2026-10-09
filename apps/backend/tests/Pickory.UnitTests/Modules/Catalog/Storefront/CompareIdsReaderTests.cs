using FluentValidation;
using Pickory.Modules.Catalog.Features.Storefront;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Storefront;

// CAT-10: đọc tham số `ids` của route so sánh. Logic thuần, không cần DB.
public sealed class CompareIdsReaderTests
{
    [Fact]
    public void Read_SplitsOnCommaAndKeepsOrder()
    {
        Assert.Equal(["c", "a", "b"], CompareIdsReader.Read("c,a,b"));
    }

    [Fact]
    public void Read_TrimsSpacesAndDropsEmptyEntries()
    {
        Assert.Equal(["a", "b"], CompareIdsReader.Read(" a , ,b,"));
    }

    [Fact]
    public void Read_DropsDuplicatesBeforeCounting()
    {
        // Năm id nhưng chỉ bốn id khác nhau, nên không vượt trần.
        Assert.Equal(["a", "b", "c", "d"], CompareIdsReader.Read("a,b,a,c,d"));
    }

    [Fact]
    public void Read_AcceptsExactlyFourIds()
    {
        Assert.Equal(4, CompareIdsReader.Read("a,b,c,d").Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    public void Read_WithNoIds_ThrowsValidationErrorOnIds(string? raw)
    {
        var error = Assert.Throws<ValidationException>(() => CompareIdsReader.Read(raw));

        Assert.Equal("ids", Assert.Single(error.Errors).PropertyName);
    }

    [Fact]
    public void Read_WithFiveDistinctIds_ThrowsValidationErrorOnIds()
    {
        var error = Assert.Throws<ValidationException>(() => CompareIdsReader.Read("a,b,c,d,e"));

        Assert.Equal("ids", Assert.Single(error.Errors).PropertyName);
    }
}
