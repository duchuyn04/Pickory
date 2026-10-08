using Pickory.Modules.Catalog.Reading;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Reading;

// CAT-10: chọn ảnh cho SKU. Ảnh riêng thắng, không có thì lấy ảnh đầu của sản phẩm.
public sealed class SkuImageChooserTests
{
    private static ImageCandidate Image(string publicId, string? skuId = null) => new(publicId, "alt", skuId);

    [Fact]
    public void Choose_PrefersImageAttachedToTheSku_EvenWhenNotFirst()
    {
        var chosen = SkuImageChooser.Choose("sku-red", [Image("first"), Image("red", "sku-red")]);

        Assert.Equal("red", chosen!.PublicId);
    }

    [Fact]
    public void Choose_WithoutOwnImage_FallsBackToFirstProductImage()
    {
        var chosen = SkuImageChooser.Choose("sku-blue", [Image("first"), Image("red", "sku-red")]);

        Assert.Equal("first", chosen!.PublicId);
    }

    [Fact]
    public void Choose_WithSeveralOwnImages_TakesTheEarliestOne()
    {
        var chosen = SkuImageChooser.Choose("sku-red", [Image("first"), Image("red-1", "sku-red"), Image("red-2", "sku-red")]);

        Assert.Equal("red-1", chosen!.PublicId);
    }

    [Fact]
    public void Choose_WithNoImages_ReturnsNull()
    {
        Assert.Null(SkuImageChooser.Choose("sku", []));
    }
}
