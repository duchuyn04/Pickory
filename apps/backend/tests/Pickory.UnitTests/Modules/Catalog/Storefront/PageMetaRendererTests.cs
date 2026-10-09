using Pickory.Modules.Catalog.Features.Storefront;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Storefront;

// CAT-11: chèn thẻ meta vào template `index.html` của SPA, mọi giá trị đều được escape HTML.
public sealed class PageMetaRendererTests
{
    private const string Template =
        "<!doctype html><html><head><meta charset=\"UTF-8\" /><title>Pickory</title></head><body><div id=\"root\"></div></body></html>";

    private static PageMeta Meta(
        string title = "Laptop | Pickory", string description = "Mô tả ngắn.",
        string? url = "https://pickory.test/p/laptop", string ogType = "product",
        string? imageUrl = "https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_1200/a",
        bool noIndex = false) =>
        new(title, description, url, ogType, imageUrl, noIndex);

    [Fact]
    public void Render_ReplacesTheTitleContent()
    {
        var html = PageMetaRenderer.Render(Template, Meta(title: "Laptop | Pickory"));

        Assert.Contains("<title>Laptop | Pickory</title>", html);
        Assert.DoesNotContain("<title>Pickory</title>", html);
    }

    [Fact]
    public void Render_InsertsDescriptionCanonicalAndOpenGraphTagsBeforeHeadEnds()
    {
        var html = PageMetaRenderer.Render(Template, Meta());

        var headEnd = html.IndexOf("</head>", StringComparison.Ordinal);
        foreach (var tag in new[]
        {
            "<meta name=\"description\" content=\"Mô tả ngắn.\" />",
            "<link rel=\"canonical\" href=\"https://pickory.test/p/laptop\" />",
            "<meta property=\"og:type\" content=\"product\" />",
            "<meta property=\"og:title\" content=\"Laptop | Pickory\" />",
            "<meta property=\"og:description\" content=\"Mô tả ngắn.\" />",
            "<meta property=\"og:url\" content=\"https://pickory.test/p/laptop\" />",
            "<meta property=\"og:image\" content=\"https://res.cloudinary.com/demo/image/upload/f_auto,q_auto,w_1200/a\" />",
            "<meta property=\"og:site_name\" content=\"Pickory\" />",
        })
        {
            var position = html.IndexOf(tag, StringComparison.Ordinal);
            Assert.True(position >= 0, $"Thiếu thẻ: {tag}");
            Assert.True(position < headEnd, $"Thẻ nằm sau </head>: {tag}");
        }
    }

    [Fact]
    public void Render_KeepsTheRestOfTheTemplate()
    {
        var html = PageMetaRenderer.Render(Template, Meta());

        Assert.Contains("<meta charset=\"UTF-8\" />", html);
        Assert.Contains("<div id=\"root\"></div>", html);
    }

    [Fact]
    public void Render_WithoutImage_OmitsOgImage()
    {
        var html = PageMetaRenderer.Render(Template, Meta(imageUrl: null));

        Assert.DoesNotContain("og:image", html);
    }

    [Fact]
    public void Render_WithoutUrl_OmitsCanonicalAndOgUrl()
    {
        var html = PageMetaRenderer.Render(Template, Meta(url: null));

        Assert.DoesNotContain("rel=\"canonical\"", html);
        Assert.DoesNotContain("og:url", html);
    }

    [Fact]
    public void Render_NoIndex_AddsRobotsMeta()
    {
        var html = PageMetaRenderer.Render(Template, Meta(noIndex: true));

        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", html);
    }

    [Fact]
    public void Render_Indexable_HasNoRobotsMeta()
    {
        var html = PageMetaRenderer.Render(Template, Meta(noIndex: false));

        Assert.DoesNotContain("robots", html);
    }

    [Fact]
    public void Render_EscapesHostileTitleAndDescription()
    {
        const string hostile = "<script>alert(1)</script> \"quote\" 'single' & more";

        var html = PageMetaRenderer.Render(Template, Meta(title: hostile, description: hostile));

        // Không có thẻ script mới và không có dấu nháy thô nào phá thuộc tính.
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("alert(1)</script>", html);
        Assert.Contains("<title>&lt;script&gt;alert(1)&lt;/script&gt; &quot;quote&quot; &#x27;single&#x27; &amp; more</title>", html);
        Assert.Contains(
            "<meta name=\"description\" content=\"&lt;script&gt;alert(1)&lt;/script&gt; &quot;quote&quot; &#x27;single&#x27; &amp; more\" />",
            html);
    }

    [Fact]
    public void Render_EscapesTheUrlAndImageAttributes()
    {
        var html = PageMetaRenderer.Render(
            Template, Meta(url: "https://pickory.test/p/a\"onload=\"x", imageUrl: "https://img.test/a\"><script>x</script>"));

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("a\"onload=\"x", html);
    }

    [Fact]
    public void Render_KeepsDollarSignsInValuesLiteral()
    {
        var html = PageMetaRenderer.Render(Template, Meta(title: "Giá $1 và $& | Pickory"));

        Assert.Contains("<title>Giá $1 và $&amp; | Pickory</title>", html);
    }

    [Fact]
    public void Render_TemplateWithoutHeadEnd_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => PageMetaRenderer.Render("<html><title>Pickory</title><body></body></html>", Meta()));

        Assert.Contains("</head>", error.Message);
    }

    [Fact]
    public void Render_TemplateWithoutTitle_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => PageMetaRenderer.Render("<html><head></head><body></body></html>", Meta()));

        Assert.Contains("<title>", error.Message);
    }
}
