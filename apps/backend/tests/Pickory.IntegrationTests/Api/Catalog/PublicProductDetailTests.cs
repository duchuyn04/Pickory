using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-10: `GET /api/products/{slug}` (kèm slug cũ) và `GET /api/products/compare`, trên bộ dữ liệu cố định.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class PublicProductDetailTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly ReadingTestData _data;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private Category _parentCategory = null!;
    private Category _childCategory = null!;
    private Category _otherCategory = null!;

    public PublicProductDetailTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        // Tắt tự đi theo chuyển hướng để test đọc được mã 301 và header `Location`.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        _data = new ReadingTestData(database, _suffix);
    }

    private string ProductId(int number) => $"dt{number:00}{_suffix}";

    private static readonly AttributeDefinition[] ParentAttributes =
    [
        new() { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = AttributeTypes.Enum, Options = ["8", "16"], Unit = "GB", Filterable = true },
        new() { Key = "weight_kg", Label = "Cân nặng", Group = "Thân máy", Type = AttributeTypes.Number, Unit = "kg", Filterable = false },
    ];

    public async Task InitializeAsync()
    {
        await _data.AddBrandAsync();
        _parentCategory = await _data.AddCategoryAsync("Laptop", null, ParentAttributes);
        _childCategory = await _data.AddCategoryAsync("Laptop gaming", _parentCategory.Id, []);
        _otherCategory = await _data.AddCategoryAsync("Điện thoại", null,
        [
            new() { Key = "storage_gb", Label = "Bộ nhớ", Group = "Cấu hình", Type = AttributeTypes.Number, Unit = "GB", Filterable = false },
        ]);
    }

    public async Task DisposeAsync() => await _data.CleanupAsync();

    private Task<ReadingTestData.SeededProduct> AddProductAsync(
        int number, Category category, ProductStatus status = ProductStatus.Active,
        IReadOnlyDictionary<string, object>? specs = null,
        IReadOnlyList<ReadingTestData.SkuSeed>? skus = null, IReadOnlyList<ReadingTestData.ImageSeed>? images = null) =>
        _data.AddProductAsync(new ReadingTestData.ProductSeed(
            ProductId(number), category.Id, status, Specs: specs, Skus: skus, Images: images));

    private async Task<(HttpResponseMessage Response, JsonElement Root)> GetJsonAsync(string path)
    {
        var response = await _client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        var root = string.IsNullOrEmpty(body) ? default : JsonDocument.Parse(body).RootElement;
        return (response, root);
    }

    private async Task AssertErrorAsync(string path, HttpStatusCode status, string code)
    {
        var (response, root) = await GetJsonAsync(path);
        Assert.True(response.StatusCode == status, $"Mong đợi {status}, nhận {response.StatusCode}: {root}");
        Assert.Equal(code, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Detail_OfCurrentSlug_Returns200WithFullDetail()
    {
        var seeded = await AddProductAsync(
            1, _childCategory,
            specs: new Dictionary<string, object> { ["ram_gb"] = "16" },
            skus: [new(20_000_000, 5, CompareAtPrice: 24_000_000), new(21_000_000, 0), new(9_000_000, 7, Enabled: false)],
            images: [new("pickory/products/b", 1), new("pickory/products/a", 0, SkuIndex: 1)]);

        var (response, root) = await GetJsonAsync($"/api/products/{seeded.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = root.GetProperty("data");
        Assert.Equal(seeded.Id, data.GetProperty("id").GetString());
        Assert.Equal("active", data.GetProperty("status").GetString());
        Assert.Equal("Mô tả ngắn", data.GetProperty("shortDesc").GetString());
        Assert.Equal("doc", data.GetProperty("description").GetProperty("type").GetString());
        Assert.Equal(12, data.GetProperty("warrantyMonths").GetInt32());
        Assert.Equal($"hang-doc-{_suffix}", data.GetProperty("brand").GetProperty("slug").GetString());
        Assert.Equal(_childCategory.Id, data.GetProperty("category").GetProperty("id").GetString());
        Assert.Equal(_parentCategory.Slug, data.GetProperty("parentCategory").GetProperty("slug").GetString());
        Assert.Equal(20_000_000, data.GetProperty("minPrice").GetInt32());
        Assert.True(data.GetProperty("inStock").GetBoolean());

        // SKU tắt không hiện cho khách. Khách chỉ nhận cờ `inStock`, không nhận số tồn chính xác.
        var skus = data.GetProperty("skus").EnumerateArray().ToList();
        Assert.Equal([seeded.SkuIds[0], seeded.SkuIds[1]], skus.Select(sku => sku.GetProperty("id").GetString()));
        Assert.Equal([true, false], skus.Select(sku => sku.GetProperty("inStock").GetBoolean()));
        Assert.Equal(24_000_000, skus[0].GetProperty("compareAtPrice").GetInt32());
        Assert.Equal("Đỏ", skus[0].GetProperty("optionValues").GetProperty("Màu").GetString());
        Assert.All(skus, sku => Assert.False(sku.TryGetProperty("stock", out _)));

        var images = data.GetProperty("images").EnumerateArray().ToList();
        Assert.Equal(2, images.Count);
        Assert.StartsWith("https://res.cloudinary.com/", images[0].GetProperty("url").GetString());
        Assert.EndsWith("/pickory/products/a", images[0].GetProperty("url").GetString());
        Assert.Equal(seeded.SkuIds[1], images[0].GetProperty("skuId").GetString());
        Assert.Equal(JsonValueKind.Null, images[1].GetProperty("skuId").ValueKind);

        // Thuộc tính hiệu lực có đủ dòng kể cả thuộc tính chưa nhập và thuộc tính không `filterable`.
        var specs = data.GetProperty("specs").EnumerateArray().ToList();
        Assert.Equal(["ram_gb", "weight_kg"], specs.Select(spec => spec.GetProperty("key").GetString()));
        Assert.Equal("16", specs[0].GetProperty("value").GetString());
        Assert.Equal("Cấu hình", specs[0].GetProperty("group").GetString());
        Assert.Equal("kg", specs[1].GetProperty("unit").GetString());
        Assert.Equal(JsonValueKind.Null, specs[1].GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task Detail_OfDiscontinuedProduct_Returns200WithStatus()
    {
        var seeded = await AddProductAsync(1, _childCategory, ProductStatus.Discontinued);

        var (response, root) = await GetJsonAsync($"/api/products/{seeded.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("discontinued", root.GetProperty("data").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Hidden)]
    public async Task Detail_OfInvisibleProduct_Returns404WithSameErrorAsUnknownSlug(ProductStatus status)
    {
        var seeded = await AddProductAsync(1, _childCategory, status);

        await AssertErrorAsync($"/api/products/{seeded.Slug}", HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    [Fact]
    public async Task Detail_OfUnknownSlug_Returns404()
    {
        await AssertErrorAsync("/api/products/khong-ton-tai-" + _suffix, HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    [Fact]
    public async Task Detail_ByProductId_Returns404BecauseTheRouteOnlyTakesSlugs()
    {
        var seeded = await AddProductAsync(1, _childCategory);

        await AssertErrorAsync($"/api/products/{seeded.Id}", HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    [Fact]
    public async Task Detail_OfOldSlug_Redirects301ToCurrentSlug()
    {
        var seeded = await AddProductAsync(1, _childCategory);
        await _data.AddSlugHistoryAsync(seeded.Id, $"ten-cu-{_suffix}");

        var response = await _client.GetAsync($"/api/products/ten-cu-{_suffix}");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"/api/products/{seeded.Slug}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Detail_OfOldSlug_KeepsTheQueryString()
    {
        var seeded = await AddProductAsync(1, _childCategory);
        await _data.AddSlugHistoryAsync(seeded.Id, $"ten-cu-{_suffix}");

        var response = await _client.GetAsync($"/api/products/ten-cu-{_suffix}?ref=email&sku=a%20b");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"/api/products/{seeded.Slug}?ref=email&sku=a%20b", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Detail_OfOldSlug_OfDiscontinuedProduct_StillRedirects()
    {
        var seeded = await AddProductAsync(1, _childCategory, ProductStatus.Discontinued);
        await _data.AddSlugHistoryAsync(seeded.Id, $"ten-cu-{_suffix}");

        var response = await _client.GetAsync($"/api/products/ten-cu-{_suffix}");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Hidden)]
    public async Task Detail_OfOldSlug_OfInvisibleProduct_Returns404WithoutRedirect(ProductStatus status)
    {
        var seeded = await AddProductAsync(1, _childCategory, status);
        await _data.AddSlugHistoryAsync(seeded.Id, $"ten-cu-{_suffix}");

        await AssertErrorAsync($"/api/products/ten-cu-{_suffix}", HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
    }

    [Fact]
    public async Task Detail_WhenSlugIsCurrentForOneProductAndOldForAnother_ServesTheCurrentOne()
    {
        var current = await AddProductAsync(1, _childCategory);
        var other = await AddProductAsync(2, _childCategory);
        await _data.AddSlugHistoryAsync(other.Id, current.Slug);

        var (response, root) = await GetJsonAsync($"/api/products/{current.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(current.Id, root.GetProperty("data").GetProperty("id").GetString());
    }

    private static string[] ComparedIds(JsonElement root) =>
        root.GetProperty("data").GetProperty("products").EnumerateArray()
            .Select(product => product.GetProperty("summary").GetProperty("id").GetString()!).ToArray();

    [Fact]
    public async Task Compare_RouteWinsOverSlugRoute()
    {
        var seeded = await AddProductAsync(1, _childCategory);

        var (response, root) = await GetJsonAsync($"/api/products/compare?ids={seeded.Id}");

        // Nếu `compare` bị coi là slug thì nhận 404 `PRODUCT_NOT_FOUND`.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([seeded.Id], ComparedIds(root));
    }

    [Fact]
    public async Task Compare_FourProductsOfSameCategory_ReturnsEverySpecInRequestedOrder()
    {
        for (var number = 1; number <= 4; number++)
        {
            await AddProductAsync(
                number, _childCategory,
                specs: new Dictionary<string, object> { ["ram_gb"] = number % 2 == 0 ? "16" : "8", ["weight_kg"] = 1.5 + number },
                images: [new($"pickory/products/p{number}", 0)]);
        }

        // Thứ tự yêu cầu khác thứ tự tạo.
        var requested = new[] { ProductId(3), ProductId(1), ProductId(4), ProductId(2) };
        var (response, root) = await GetJsonAsync($"/api/products/compare?ids={string.Join(',', requested)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(requested, ComparedIds(root));
        var data = root.GetProperty("data");
        Assert.Equal(_childCategory.Id, data.GetProperty("category").GetProperty("id").GetString());

        // Thuộc tính của danh mục cha đủ mọi khóa, kể cả `weight_kg` không `filterable`. Nhóm theo thứ tự xuất hiện.
        var groups = data.GetProperty("attributeGroups").EnumerateArray().ToList();
        Assert.Equal(["Cấu hình", "Thân máy"], groups.Select(group => group.GetProperty("name").GetString()));
        Assert.Equal("weight_kg", groups[1].GetProperty("attributes")[0].GetProperty("key").GetString());
        Assert.False(groups[1].GetProperty("attributes")[0].GetProperty("filterable").GetBoolean());

        var products = data.GetProperty("products").EnumerateArray().ToList();
        Assert.All(products, product =>
        {
            var specs = product.GetProperty("specs");
            Assert.True(specs.TryGetProperty("ram_gb", out _));
            Assert.True(specs.TryGetProperty("weight_kg", out _));
            Assert.StartsWith("https://res.cloudinary.com/", product.GetProperty("summary").GetProperty("image").GetProperty("url").GetString());
        });
        Assert.Equal("8", products[0].GetProperty("specs").GetProperty("ram_gb").GetString());
        Assert.Equal(4.5, products[0].GetProperty("specs").GetProperty("weight_kg").GetDouble());
        Assert.Equal("16", products[2].GetProperty("specs").GetProperty("ram_gb").GetString());
    }

    [Fact]
    public async Task Compare_SpecMissingOnAProduct_IsNullButKeyIsPresent()
    {
        await AddProductAsync(1, _childCategory, specs: new Dictionary<string, object> { ["ram_gb"] = "8" });

        var (_, root) = await GetJsonAsync($"/api/products/compare?ids={ProductId(1)}");

        var specs = root.GetProperty("data").GetProperty("products")[0].GetProperty("specs");
        Assert.Equal(JsonValueKind.Null, specs.GetProperty("weight_kg").ValueKind);
    }

    [Fact]
    public async Task Compare_ProductsOfDifferentCategories_Returns400CategoryMismatch()
    {
        await AddProductAsync(1, _childCategory);
        await AddProductAsync(2, _otherCategory);

        await AssertErrorAsync(
            $"/api/products/compare?ids={ProductId(1)},{ProductId(2)}", HttpStatusCode.BadRequest, "COMPARE_CATEGORY_MISMATCH");
    }

    [Fact]
    public async Task Compare_ParentAndChildCategoryProducts_AreNotTheSameLeafCategory()
    {
        await AddProductAsync(1, _childCategory);
        await AddProductAsync(2, _parentCategory);

        await AssertErrorAsync(
            $"/api/products/compare?ids={ProductId(1)},{ProductId(2)}", HttpStatusCode.BadRequest, "COMPARE_CATEGORY_MISMATCH");
    }

    [Fact]
    public async Task Compare_MoreThanFourIds_Returns400ValidationError()
    {
        for (var number = 1; number <= 5; number++)
        {
            await AddProductAsync(number, _childCategory);
        }

        var ids = string.Join(',', Enumerable.Range(1, 5).Select(ProductId));
        await AssertErrorAsync($"/api/products/compare?ids={ids}", HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("/api/products/compare")]
    [InlineData("/api/products/compare?ids=")]
    [InlineData("/api/products/compare?ids=,,")]
    public async Task Compare_WithoutIds_Returns400ValidationError(string path)
    {
        await AssertErrorAsync(path, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Compare_DuplicateIds_AreCountedOnce()
    {
        for (var number = 1; number <= 5; number++)
        {
            await AddProductAsync(number, _childCategory);
        }

        // Sáu id nhưng chỉ bốn id khác nhau, nên hợp lệ.
        var ids = $"{ProductId(1)},{ProductId(2)},{ProductId(1)},{ProductId(3)},{ProductId(2)},{ProductId(4)}";
        var (response, root) = await GetJsonAsync($"/api/products/compare?ids={ids}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([ProductId(1), ProductId(2), ProductId(3), ProductId(4)], ComparedIds(root));
    }

    [Fact]
    public async Task Compare_RepeatedIdsParameter_IsJoinedIntoOneList()
    {
        await AddProductAsync(1, _childCategory);
        await AddProductAsync(2, _childCategory);

        var (response, root) = await GetJsonAsync($"/api/products/compare?ids={ProductId(2)}&ids={ProductId(1)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([ProductId(2), ProductId(1)], ComparedIds(root));
    }

    [Fact]
    public async Task Compare_SkipsUnknownAndHiddenProducts_SoOldLinksStillOpen()
    {
        await AddProductAsync(1, _childCategory);
        await AddProductAsync(2, _childCategory, ProductStatus.Hidden);
        await AddProductAsync(3, _childCategory, ProductStatus.Discontinued);

        var ids = $"{ProductId(1)},khong-ton-tai,{ProductId(2)},{ProductId(3)}";
        var (response, root) = await GetJsonAsync($"/api/products/compare?ids={ids}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([ProductId(1), ProductId(3)], ComparedIds(root));
    }

    [Fact]
    public async Task Compare_InvisibleProductOfAnotherCategory_DoesNotCauseCategoryMismatch()
    {
        await AddProductAsync(1, _childCategory);
        await AddProductAsync(2, _otherCategory, ProductStatus.Draft);
        await AddProductAsync(3, _otherCategory, ProductStatus.Hidden);

        var (response, root) = await GetJsonAsync($"/api/products/compare?ids={ProductId(1)},{ProductId(2)},{ProductId(3)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([ProductId(1)], ComparedIds(root));
    }

    [Fact]
    public async Task Compare_WhenNoProductIsVisible_Returns200WithEmptyResult()
    {
        await AddProductAsync(1, _childCategory, ProductStatus.Hidden);

        var (response, root) = await GetJsonAsync($"/api/products/compare?ids={ProductId(1)},khong-ton-tai");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = root.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("category").ValueKind);
        Assert.Empty(data.GetProperty("products").EnumerateArray());
        Assert.Empty(data.GetProperty("attributeGroups").EnumerateArray());
    }
}
