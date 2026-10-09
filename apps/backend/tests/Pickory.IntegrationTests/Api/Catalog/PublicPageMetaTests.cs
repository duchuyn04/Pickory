using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-11: `GET /p/{slug}` và `GET /c/{slug}` trả template của SPA kèm thẻ meta SEO.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class PublicPageMetaTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string WebOrigin = "https://pickory.test";

    private const string Template =
        "<!doctype html><html lang=\"vi\"><head><meta charset=\"UTF-8\" /><title>Pickory</title></head>"
        + "<body><div id=\"root\"></div></body></html>";

    private readonly PickoryWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly ReadingTestData _data;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private readonly string _templatePath;
    private Category _category = null!;

    public PublicPageMetaTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _templatePath = Path.Combine(Path.GetTempPath(), $"pickory-index-{_suffix}.html");
        File.WriteAllText(_templatePath, Template);
        _factory = factory;
        // Tắt tự đi theo chuyển hướng để test đọc được mã 301 và header `Location`.
        _client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("WEB_INDEX_HTML_PATH", _templatePath);
                builder.UseSetting("WEB_ORIGIN", WebOrigin);
            })
            .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        _data = new ReadingTestData(database, _suffix);
    }

    private string ProductId(int number) => $"pg{number:00}{_suffix}";

    public async Task InitializeAsync()
    {
        await _data.AddBrandAsync();
        _category = await _data.AddCategoryAsync("Laptop", null, []);
    }

    public async Task DisposeAsync()
    {
        File.Delete(_templatePath);
        await _data.CleanupAsync();
    }

    private Task<ReadingTestData.SeededProduct> AddProductAsync(
        int number, ProductStatus status = ProductStatus.Active, string? name = null, string? shortDesc = null,
        string? descriptionJson = null, IReadOnlyList<ReadingTestData.ImageSeed>? images = null) =>
        _data.AddProductAsync(new ReadingTestData.ProductSeed(
            ProductId(number), _category.Id, status, Images: images, Name: name, ShortDesc: shortDesc,
            DescriptionJson: descriptionJson));

    private async Task<(HttpResponseMessage Response, string Html)> GetHtmlAsync(string path)
    {
        var response = await _client.GetAsync(path);
        return (response, await response.Content.ReadAsStringAsync());
    }

    private static void AssertIsHtml(HttpResponseMessage response) =>
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());

    private static void AssertDefaultNotFoundMeta(HttpResponseMessage response, string html)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertIsHtml(response);
        Assert.Contains("<title>Không tìm thấy | Pickory</title>", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", html);
        Assert.DoesNotContain("og:image", html);
        Assert.DoesNotContain("rel=\"canonical\"", html);
        // SPA vẫn có điểm gắn `#root` để tự hiện trang "Không tìm thấy".
        Assert.Contains("<div id=\"root\"></div>", html);
    }

    [Fact]
    public async Task ProductPage_OfActiveProduct_Returns200WithProductMeta()
    {
        var seeded = await AddProductAsync(
            1, name: "Laptop Kaze Air", shortDesc: "Mỏng nhẹ, pin lâu.",
            images: [new("pickory/products/main", 0)]);

        var (response, html) = await GetHtmlAsync($"/p/{seeded.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertIsHtml(response);
        Assert.Contains("<title>Laptop Kaze Air | Pickory</title>", html);
        Assert.Contains("<meta name=\"description\" content=\"Mỏng nhẹ, pin lâu.\" />", html);
        Assert.Contains($"<link rel=\"canonical\" href=\"{WebOrigin}/p/{seeded.Slug}\" />", html);
        Assert.Contains("<meta property=\"og:type\" content=\"product\" />", html);
        Assert.Contains($"<meta property=\"og:url\" content=\"{WebOrigin}/p/{seeded.Slug}\" />", html);
        Assert.Contains("/image/upload/f_auto,q_auto,w_1200/pickory/products/main\" />", html);
        Assert.DoesNotContain("robots", html);
        Assert.Contains("<div id=\"root\"></div>", html);
    }

    [Fact]
    public async Task ProductPage_OfDiscontinuedProduct_Returns200()
    {
        var seeded = await AddProductAsync(2, ProductStatus.Discontinued, name: "Laptop Cũ");

        var (response, html) = await GetHtmlAsync($"/p/{seeded.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Laptop Cũ | Pickory</title>", html);
    }

    [Fact]
    public async Task ProductPage_CanonicalIgnoresTheQueryString()
    {
        var seeded = await AddProductAsync(3);

        var (_, html) = await GetHtmlAsync($"/p/{seeded.Slug}?sku=abc&ref=mail");

        Assert.Contains($"<link rel=\"canonical\" href=\"{WebOrigin}/p/{seeded.Slug}\" />", html);
        Assert.DoesNotContain("sku=abc", html);
    }

    [Fact]
    public async Task ProductPage_WithoutShortDescription_UsesPlainTextOfTheDescription()
    {
        const string description =
            """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Mô tả dài của sản phẩm."}]}]}""";
        var seeded = await AddProductAsync(4, shortDesc: "", descriptionJson: description);

        var (_, html) = await GetHtmlAsync($"/p/{seeded.Slug}");

        Assert.Contains("<meta name=\"description\" content=\"Mô tả dài của sản phẩm.\" />", html);
    }

    [Fact]
    public async Task ProductPage_WithoutImages_HasNoOgImage()
    {
        var seeded = await AddProductAsync(5);

        var (_, html) = await GetHtmlAsync($"/p/{seeded.Slug}");

        Assert.DoesNotContain("og:image\"", html);
    }

    [Fact]
    public async Task ProductPage_EscapesHtmlInNameAndDescription()
    {
        // Tiêu chí 1: tên và mô tả ngắn là dữ liệu nhân viên nhập, nên mọi ký tự HTML phải bị escape.
        const string hostile = "<script>alert(1)</script> \"quote\" 'single' & more";
        var seeded = await AddProductAsync(6, name: hostile, shortDesc: hostile);

        var (response, html) = await GetHtmlAsync($"/p/{seeded.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("alert(1)</script>", html);
        const string escaped = "&lt;script&gt;alert(1)&lt;/script&gt; &quot;quote&quot; &#x27;single&#x27; &amp; more";
        Assert.Contains($"<title>{escaped} | Pickory</title>", html);
        Assert.Contains($"<meta name=\"description\" content=\"{escaped}\" />", html);
        Assert.Contains($"<meta property=\"og:title\" content=\"{escaped} | Pickory\" />", html);
    }

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Hidden)]
    public async Task ProductPage_OfInvisibleProduct_Returns404WithDefaultMeta(ProductStatus status)
    {
        var seeded = await AddProductAsync(7, status, name: "Sản phẩm bí mật");

        var (response, html) = await GetHtmlAsync($"/p/{seeded.Slug}");

        AssertDefaultNotFoundMeta(response, html);
        // Không lộ tên của sản phẩm khách không thấy được.
        Assert.DoesNotContain("Sản phẩm bí mật", html);
    }

    [Fact]
    public async Task ProductPage_OfUnknownSlug_Returns404WithDefaultMeta()
    {
        var (response, html) = await GetHtmlAsync($"/p/khong-ton-tai-{_suffix}");

        AssertDefaultNotFoundMeta(response, html);
    }

    [Fact]
    public async Task ProductPage_OfOldSlug_Redirects301ToCurrentSlug()
    {
        var seeded = await AddProductAsync(8);
        await _data.AddSlugHistoryAsync(seeded.Id, $"ten-cu-{_suffix}");

        var response = await _client.GetAsync($"/p/ten-cu-{_suffix}");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"/p/{seeded.Slug}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ProductPage_OfOldSlug_KeepsTheQueryString()
    {
        var seeded = await AddProductAsync(9);
        await _data.AddSlugHistoryAsync(seeded.Id, $"ten-cu-{_suffix}");

        var response = await _client.GetAsync($"/p/ten-cu-{_suffix}?sku=abc&ref=mail");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"/p/{seeded.Slug}?sku=abc&ref=mail", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Hidden)]
    public async Task ProductPage_OfOldSlugOfInvisibleProduct_Returns404WithoutRedirect(ProductStatus status)
    {
        var seeded = await AddProductAsync(10, status);
        await _data.AddSlugHistoryAsync(seeded.Id, $"ten-cu-{_suffix}");

        var (response, html) = await GetHtmlAsync($"/p/ten-cu-{_suffix}");

        AssertDefaultNotFoundMeta(response, html);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task CategoryPage_Returns200WithCategoryMeta()
    {
        var (response, html) = await GetHtmlAsync($"/c/{_category.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertIsHtml(response);
        Assert.Contains($"<title>{_category.Name} | Pickory</title>", html);
        Assert.Contains($"<meta name=\"description\" content=\"Mua {_category.Name} chính hãng tại Pickory.\" />", html);
        Assert.Contains("<meta property=\"og:type\" content=\"website\" />", html);
        Assert.Contains($"<link rel=\"canonical\" href=\"{WebOrigin}/c/{_category.Slug}\" />", html);
        Assert.DoesNotContain("og:image", html);
        Assert.DoesNotContain("robots", html);
    }

    [Fact]
    public async Task CategoryPage_OfUnknownSlug_Returns404WithDefaultMeta()
    {
        var (response, html) = await GetHtmlAsync($"/c/khong-ton-tai-{_suffix}");

        AssertDefaultNotFoundMeta(response, html);
    }

    [Fact]
    public async Task Pages_WithoutWebIndexHtmlPath_AreNotMapped()
    {
        // Factory gốc không đặt `WEB_INDEX_HTML_PATH`, nên hai route HTML không tồn tại.
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        foreach (var path in new[] { "/p/bat-ky", "/c/bat-ky" })
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
            // Route không tồn tại nên middleware lỗi chung trả JSON `NOT_FOUND`, không phải trang HTML.
            Assert.Contains("\"code\":\"NOT_FOUND\"", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public void Startup_WithMissingTemplateFile_FailsWithClearMessage()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"khong-co-{_suffix}.html");
        using var factory = _factory.WithWebHostBuilder(
            builder => builder.UseSetting("WEB_INDEX_HTML_PATH", missingPath));

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("WEB_INDEX_HTML_PATH", error.ToString());
    }
}
