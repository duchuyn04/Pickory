using System.Text.Json;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Features.Storefront;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Storefront;

// CAT-11: dựng nội dung meta cho sản phẩm, danh mục và trang không tìm thấy.
public sealed class PageMetaFactoryTests
{
    private const string WebOrigin = "https://pickory.test";

    private static ProductDetail Product(
        string name = "Laptop Kaze", string shortDesc = "Mỏng nhẹ, pin lâu.",
        string descriptionJson = """{"type":"doc","content":[{"type":"paragraph"}]}""",
        string status = "active", IReadOnlyList<ProductImageInfo>? images = null) =>
        new(
            "p1", "laptop-kaze", name, status, shortDesc, JsonDocument.Parse(descriptionJson).RootElement.Clone(), 12,
            new BrandRef("b1", "Kaze", "kaze"), new CategoryRef("c1", "Laptop", "laptop"), null,
            [], [], [], images ?? [], null, true);

    [Fact]
    public void ForProduct_BuildsTitleUrlAndType()
    {
        var meta = PageMetaFactory.ForProduct(Product(), WebOrigin);

        Assert.Equal("Laptop Kaze | Pickory", meta.Title);
        Assert.Equal("https://pickory.test/p/laptop-kaze", meta.Url);
        Assert.Equal("product", meta.OgType);
        Assert.False(meta.NoIndex);
    }

    [Fact]
    public void ForProduct_UsesShortDescription()
    {
        var meta = PageMetaFactory.ForProduct(Product(shortDesc: "Mỏng nhẹ, pin lâu."), WebOrigin);

        Assert.Equal("Mỏng nhẹ, pin lâu.", meta.Description);
    }

    [Fact]
    public void ForProduct_WithoutShortDescription_FallsBackToPlainTextOfTheDescription()
    {
        var json = """{"type":"doc","content":[{"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Tiêu đề"}]},{"type":"paragraph","content":[{"type":"text","text":"Nội dung "},{"type":"text","text":"đậm","marks":[{"type":"bold"}]}]}]}""";

        var meta = PageMetaFactory.ForProduct(Product(shortDesc: "", descriptionJson: json), WebOrigin);

        // Các khối cách nhau bằng một khoảng trắng, không còn dòng trống.
        Assert.Equal("Tiêu đề Nội dung đậm", meta.Description);
    }

    [Fact]
    public void ForProduct_WithNoDescriptionAtAll_UsesTheShopDescription()
    {
        var meta = PageMetaFactory.ForProduct(Product(shortDesc: "  "), WebOrigin);

        Assert.Equal(PageMetaFactory.ShopDescription, meta.Description);
    }

    [Fact]
    public void ForProduct_UsesFirstImageWithCloudinaryTransform()
    {
        var images = new[]
        {
            new ProductImageInfo("https://res.cloudinary.com/demo/image/upload/pickory/products/a", "A", null),
            new ProductImageInfo("https://res.cloudinary.com/demo/image/upload/pickory/products/b", "B", null),
        };

        var meta = PageMetaFactory.ForProduct(Product(images: images), WebOrigin);

        Assert.Equal(
            "https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_1200/pickory/products/a", meta.ImageUrl);
    }

    [Fact]
    public void ForProduct_WithoutImages_HasNoImageUrl()
    {
        Assert.Null(PageMetaFactory.ForProduct(Product(images: []), WebOrigin).ImageUrl);
    }

    [Fact]
    public void ForProduct_ImageUrlWithoutUploadSegment_IsKeptAsIs()
    {
        var images = new[] { new ProductImageInfo("https://cdn.test/a.png", "A", null) };

        Assert.Equal("https://cdn.test/a.png", PageMetaFactory.ForProduct(Product(images: images), WebOrigin).ImageUrl);
    }

    [Fact]
    public void ForProduct_TrailingSlashInWebOrigin_DoesNotDoubleTheSlash()
    {
        var meta = PageMetaFactory.ForProduct(Product(), "https://pickory.test/");

        Assert.Equal("https://pickory.test/p/laptop-kaze", meta.Url);
    }

    [Fact]
    public void ForCategory_BuildsFixedDescriptionAndWebsiteType()
    {
        var meta = PageMetaFactory.ForCategory("Laptop", "laptop-gaming", WebOrigin);

        Assert.Equal("Laptop | Pickory", meta.Title);
        Assert.Equal("Mua Laptop chính hãng tại Pickory.", meta.Description);
        Assert.Equal("https://pickory.test/c/laptop-gaming", meta.Url);
        Assert.Equal("website", meta.OgType);
        Assert.Null(meta.ImageUrl);
        Assert.False(meta.NoIndex);
    }

    [Fact]
    public void NotFound_HasDefaultTitleNoImageAndNoIndex()
    {
        var meta = PageMetaFactory.NotFound();

        Assert.Equal("Không tìm thấy | Pickory", meta.Title);
        Assert.Equal(PageMetaFactory.ShopDescription, meta.Description);
        Assert.Null(meta.ImageUrl);
        Assert.Null(meta.Url);
        Assert.True(meta.NoIndex);
    }

    [Fact]
    public void TruncateDescription_ShortText_IsUnchanged()
    {
        Assert.Equal("Ngắn gọn.", PageMetaFactory.TruncateDescription("Ngắn gọn."));
    }

    [Fact]
    public void TruncateDescription_ExactlyAtTheLimit_IsUnchanged()
    {
        var text = new string('a', PageMetaFactory.MaxDescriptionLength);

        Assert.Equal(text, PageMetaFactory.TruncateDescription(text));
    }

    [Fact]
    public void TruncateDescription_LongText_CutsAtAWordBoundaryWithinTheLimit()
    {
        var text = string.Join(' ', Enumerable.Repeat("chữ", 100));

        var result = PageMetaFactory.TruncateDescription(text);

        Assert.True(result.Length <= PageMetaFactory.MaxDescriptionLength, $"Dài {result.Length}");
        Assert.EndsWith("…", result);
        // Phần còn lại sau khi bỏ dấu ba chấm chỉ gồm những từ nguyên vẹn.
        Assert.All(result[..^1].Split(' '), word => Assert.Equal("chữ", word));
    }

    [Fact]
    public void TruncateDescription_LongTextWithoutSpaces_CutsAtTheLimit()
    {
        var result = PageMetaFactory.TruncateDescription(new string('a', 400));

        Assert.Equal(PageMetaFactory.MaxDescriptionLength, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void TruncateDescription_CollapsesWhitespaceAndNewlines()
    {
        Assert.Equal("một hai ba", PageMetaFactory.TruncateDescription("  một\n\nhai \t ba  "));
    }
}
