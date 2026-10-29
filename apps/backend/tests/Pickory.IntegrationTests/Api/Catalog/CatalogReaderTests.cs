using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Identity.Roles;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-10: `ICatalogReader` trên bộ dữ liệu cố định. Gọi qua DI giống cách module `commerce` và `smart-search` sẽ gọi.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CatalogReaderTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly PickoryWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly ReadingTestData _data;
    private readonly StockTestData _stockData;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private Category _parentCategory = null!;
    private Category _childCategory = null!;

    public CatalogReaderTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _data = new ReadingTestData(database, _suffix);
        _stockData = new StockTestData(database, _suffix);
    }

    // Id có số thứ tự để thứ tự theo `Id` tính được trong test.
    private string ProductId(int number) => $"rd{number:00}{_suffix}";

    private static readonly AttributeDefinition[] ParentAttributes =
    [
        new() { Key = "ram_gb", Label = "RAM", Group = "Cấu hình", Type = AttributeTypes.Enum, Options = ["8", "16"], Unit = "GB", Filterable = true },
        new() { Key = "weight_kg", Label = "Cân nặng", Group = "Thân máy", Type = AttributeTypes.Number, Unit = "kg", Filterable = false },
    ];

    private static readonly AttributeDefinition[] ChildAttributes =
    [
        new() { Key = "gpu", Label = "Card đồ họa", Group = "Cấu hình", Type = AttributeTypes.Text, Filterable = false },
    ];

    public async Task InitializeAsync()
    {
        await _data.AddBrandAsync();
        _parentCategory = await _data.AddCategoryAsync("Laptop", null, ParentAttributes);
        _childCategory = await _data.AddCategoryAsync("Laptop gaming", _parentCategory.Id, ChildAttributes);
        await _stockData.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await StockInvariant.AssertHoldsAsync(_database);
        await _stockData.CleanupAsync();
        await _data.CleanupAsync();
    }

    private async Task<T> WithReaderAsync<T>(Func<ICatalogReader, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICatalogReader>());
    }

    private Task<ReadingTestData.SeededProduct> AddProductAsync(
        int number, ProductStatus status = ProductStatus.Active, bool inChild = true,
        IReadOnlyList<ReadingTestData.SkuSeed>? skus = null, IReadOnlyList<ReadingTestData.ImageSeed>? images = null,
        IReadOnlyDictionary<string, object>? specs = null) =>
        _data.AddProductAsync(new ReadingTestData.ProductSeed(
            ProductId(number), inChild ? _childCategory.Id : _parentCategory.Id, status, Specs: specs, Skus: skus, Images: images));

    [Fact]
    public async Task GetProducts_KeepsRequestedOrder_AndDropsDuplicatesUnknownAndInvisibleProducts()
    {
        await AddProductAsync(1);
        await AddProductAsync(2, ProductStatus.Discontinued);
        await AddProductAsync(3, ProductStatus.Draft);
        await AddProductAsync(4, ProductStatus.Hidden);
        await AddProductAsync(5);

        // Thứ tự xáo trộn, có id trùng, id lạ, id rỗng, và hai id sản phẩm khách không thấy.
        var ids = new[] { ProductId(5), ProductId(2), ProductId(5), "khong-ton-tai", ProductId(3), "", ProductId(1), ProductId(4) };
        var products = await WithReaderAsync(reader => reader.GetProductsAsync(ids, CancellationToken.None));

        Assert.Equal([ProductId(5), ProductId(2), ProductId(1)], products.Select(product => product.Id));
        Assert.Equal("discontinued", products[1].Status);
    }

    [Fact]
    public async Task GetProductCategories_KeepsRequestedOrder_AndDropsDuplicatesUnknownAndInvisibleProducts()
    {
        await AddProductAsync(1);
        await AddProductAsync(2, inChild: false);
        await AddProductAsync(3, ProductStatus.Draft);

        // Thứ tự xáo trộn, có id trùng, id lạ và một sản phẩm khách không thấy.
        var ids = new[] { ProductId(2), ProductId(1), ProductId(2), "khong-ton-tai", ProductId(3) };
        var categories = await WithReaderAsync(
            reader => reader.GetProductCategoriesAsync(ids, CancellationToken.None));

        Assert.Equal([ProductId(2), ProductId(1)], categories.Select(item => item.ProductId));
        Assert.Equal(_parentCategory.Id, categories[0].Category.Id);
        Assert.Equal(_parentCategory.Name, categories[0].Category.Name);
        Assert.Equal(_childCategory.Id, categories[1].Category.Id);
        Assert.Equal(_childCategory.Name, categories[1].Category.Name);
    }

    [Fact]
    public async Task GetProducts_WithNoValidIds_ReturnsEmpty()
    {
        await AddProductAsync(1, ProductStatus.Draft);

        var products = await WithReaderAsync(reader => reader.GetProductsAsync([ProductId(1), "ghost"], CancellationToken.None));
        var empty = await WithReaderAsync(reader => reader.GetProductsAsync([], CancellationToken.None));

        Assert.Empty(products);
        Assert.Empty(empty);
    }

    [Fact]
    public async Task GetProducts_ReturnsFirstImageWithUrl()
    {
        await AddProductAsync(1, images: [new("pickory/products/second", 1), new("pickory/products/first", 0)]);

        var products = await WithReaderAsync(reader => reader.GetProductsAsync([ProductId(1)], CancellationToken.None));

        var image = Assert.Single(products).Image!;
        Assert.EndsWith("/image/upload/pickory/products/first", image.Url);
        Assert.StartsWith("https://res.cloudinary.com/", image.Url);
        Assert.Equal("Alt pickory/products/first", image.Alt);
    }

    [Fact]
    public async Task GetProduct_ByIdAndBySlug_ReturnsFullDetail()
    {
        var seeded = await AddProductAsync(
            1,
            skus: [new(20_000_000, 5, CompareAtPrice: 25_000_000), new(18_000_000, 0), new(15_000_000, 9, Enabled: false)],
            images: [new("pickory/products/b", 1), new("pickory/products/a", 0), new("pickory/products/red", 2, SkuIndex: 0)],
            specs: new Dictionary<string, object> { ["ram_gb"] = "16", ["gpu"] = "RTX" });

        var byId = await WithReaderAsync(reader => reader.GetProductAsync(seeded.Id, CancellationToken.None));
        var bySlug = await WithReaderAsync(reader => reader.GetProductAsync(seeded.Slug, CancellationToken.None));

        Assert.NotNull(byId);
        Assert.Equal(byId.Id, bySlug!.Id);
        Assert.Equal("active", byId.Status);
        Assert.Equal(_childCategory.Id, byId.Category.Id);
        Assert.Equal(_parentCategory.Id, byId.ParentCategory!.Id);
        Assert.Equal($"hang-doc-{_suffix}", byId.Brand.Slug);
        // SKU tắt không hiện cho khách. `MinPrice` do CAT-05 tính, bỏ qua SKU hết hàng khi còn SKU có hàng.
        Assert.Equal(2, byId.Skus.Count);
        Assert.DoesNotContain(byId.Skus, sku => sku.Id == seeded.SkuIds[2]);
        Assert.Equal([true, false], byId.Skus.Select(sku => sku.InStock));
        Assert.Equal([5, 0], byId.Skus.Select(sku => sku.Stock));
        Assert.Equal(25_000_000, byId.Skus[0].CompareAtPrice);
        Assert.Equal(20_000_000, byId.MinPrice);
        Assert.Equal(["a", "b", "red"], byId.Images.Select(image => image.Url.Split('/')[^1]));
        Assert.Equal(seeded.SkuIds[0], byId.Images[2].SkuId);
        Assert.Equal("Màu", Assert.Single(byId.Options).Name);
    }

    [Fact]
    public async Task GetProduct_Specs_ListEveryEffectiveAttributeWithParentFirst()
    {
        var seeded = await AddProductAsync(1, specs: new Dictionary<string, object> { ["ram_gb"] = "16", ["gpu"] = "RTX" });

        var detail = await WithReaderAsync(reader => reader.GetProductAsync(seeded.Id, CancellationToken.None));

        Assert.Equal(["ram_gb", "weight_kg", "gpu"], detail!.Specs.Select(spec => spec.Key));
        Assert.Equal("16", detail.Specs[0].Value!.Value.GetString());
        // Sản phẩm chưa nhập cân nặng, nhưng dòng thông số vẫn có để bảng đủ hàng.
        Assert.Null(detail.Specs[1].Value);
        Assert.Equal("Thân máy", detail.Specs[1].Group);
        Assert.Equal("kg", detail.Specs[1].Unit);
        Assert.Equal("RTX", detail.Specs[2].Value!.Value.GetString());
    }

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Hidden)]
    public async Task GetProduct_OfInvisibleProduct_ReturnsNull(ProductStatus status)
    {
        var seeded = await AddProductAsync(1, status);

        Assert.Null(await WithReaderAsync(reader => reader.GetProductAsync(seeded.Id, CancellationToken.None)));
        Assert.Null(await WithReaderAsync(reader => reader.GetProductAsync(seeded.Slug, CancellationToken.None)));
    }

    [Fact]
    public async Task GetProduct_OfDiscontinuedProduct_ReturnsDetailWithStatus()
    {
        var seeded = await AddProductAsync(1, ProductStatus.Discontinued);

        var detail = await WithReaderAsync(reader => reader.GetProductAsync(seeded.Id, CancellationToken.None));

        Assert.Equal("discontinued", detail!.Status);
    }

    [Fact]
    public async Task GetProduct_OfUnknownIdOrSlug_ReturnsNull()
    {
        Assert.Null(await WithReaderAsync(reader => reader.GetProductAsync("khong-ton-tai", CancellationToken.None)));
    }

    [Fact]
    public async Task GetSkus_KeepsOrder_AndReturnsDisabledSkuAndInvisibleProductWithFlags()
    {
        var active = await AddProductAsync(
            1,
            skus: [new(20_000_000, 4, CompareAtPrice: 22_000_000), new(21_000_000, 7, Enabled: false), new(19_000_000, 0)],
            images: [new("pickory/products/main", 0), new("pickory/products/red", 1, SkuIndex: 0)]);
        var draft = await AddProductAsync(2, ProductStatus.Draft, skus: [new(5_000_000, 3)]);
        var discontinued = await AddProductAsync(3, ProductStatus.Discontinued, skus: [new(6_000_000, 2)]);

        // Thứ tự xáo trộn, có id lạ và id trùng.
        var requested = new[]
        {
            discontinued.SkuIds[0], active.SkuIds[2], "khong-ton-tai", active.SkuIds[0], draft.SkuIds[0], active.SkuIds[1], active.SkuIds[0],
        };
        var snapshots = await WithReaderAsync(reader => reader.GetSkusAsync(requested, CancellationToken.None));

        Assert.Equal(
            [discontinued.SkuIds[0], active.SkuIds[2], active.SkuIds[0], draft.SkuIds[0], active.SkuIds[1]],
            snapshots.Select(snapshot => snapshot.SkuId));

        var enabledSku = snapshots.Single(snapshot => snapshot.SkuId == active.SkuIds[0]);
        Assert.True(enabledSku.Enabled);
        Assert.Equal(20_000_000, enabledSku.Price);
        Assert.Equal(22_000_000, enabledSku.CompareAtPrice);
        // Commerce cần số tồn chính xác, khác với chi tiết công khai chỉ có cờ còn hàng.
        Assert.Equal(4, enabledSku.Stock);
        Assert.Equal("active", enabledSku.ProductStatus);
        Assert.Equal(ProductId(1), enabledSku.ProductId);
        Assert.Equal($"Sản phẩm {ProductId(1)}", enabledSku.ProductName);
        Assert.Equal("Đỏ", enabledSku.OptionValues["Màu"]);
        Assert.Equal(300, enabledSku.WeightGrams);
        Assert.Equal(20, enabledSku.LengthCm);
        Assert.Equal(10, enabledSku.WidthCm);
        Assert.Equal(2, enabledSku.HeightCm);

        var disabledSku = snapshots.Single(snapshot => snapshot.SkuId == active.SkuIds[1]);
        Assert.False(disabledSku.Enabled);
        Assert.Equal("active", disabledSku.ProductStatus);

        Assert.Equal("draft", snapshots.Single(snapshot => snapshot.SkuId == draft.SkuIds[0]).ProductStatus);
        Assert.Equal("discontinued", snapshots.Single(snapshot => snapshot.SkuId == discontinued.SkuIds[0]).ProductStatus);
    }

    [Fact]
    public async Task GetSkus_SoftDeletedProduct_RemainsReadableWithDeletionFlag()
    {
        var product = await AddProductAsync(1, skus: [new(20_000_000, 4)]);
        await using (var context = _database.CreateCatalogContext())
        {
            var entity = await context.Products.SingleAsync(item => item.Id == product.Id);
            entity.DeletedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        var snapshots = await WithReaderAsync(reader => reader.GetSkusAsync(
            product.SkuIds, CancellationToken.None));
        var snapshot = Assert.Single(snapshots);

        // Đơn cũ vẫn đọc được kích thước, nhưng giỏ mới nhận cờ để chặn mua sản phẩm đã xóa.
        Assert.True(snapshot.ProductDeleted);
        Assert.Equal(300, snapshot.WeightGrams);
        Assert.Null(await WithReaderAsync(reader => reader.GetProductAsync(product.Id, CancellationToken.None)));
    }

    [Fact]
    public async Task GetSkus_UsesOwnImageWhenPresent_AndFirstProductImageOtherwise()
    {
        var product = await AddProductAsync(
            1,
            skus: [new(1_000_000, 1), new(1_000_000, 1)],
            images: [new("pickory/products/main", 0), new("pickory/products/red", 1, SkuIndex: 0)]);
        var withoutImages = await AddProductAsync(2, skus: [new(1_000_000, 1)]);

        var snapshots = await WithReaderAsync(reader => reader.GetSkusAsync(
            [product.SkuIds[0], product.SkuIds[1], withoutImages.SkuIds[0]], CancellationToken.None));

        Assert.EndsWith("/pickory/products/red", snapshots[0].Image!.Url);
        Assert.Equal(product.SkuIds[0], snapshots[0].Image!.SkuId);
        Assert.EndsWith("/pickory/products/main", snapshots[1].Image!.Url);
        Assert.Null(snapshots[2].Image);
    }

    [Fact]
    public async Task GetSkus_WithNoIds_ReturnsEmpty()
    {
        Assert.Empty(await WithReaderAsync(reader => reader.GetSkusAsync([], CancellationToken.None)));
    }

    [Fact]
    public async Task ListForIndexing_WalksEveryVisibleProductOnceAcrossPages()
    {
        // Mười sản phẩm khách thấy được và ba sản phẩm không thấy. Trang nhỏ ép phải qua nhiều trang.
        var visibleIds = new List<string>();
        for (var number = 1; number <= 10; number++)
        {
            var status = number % 4 == 0 ? ProductStatus.Discontinued : ProductStatus.Active;
            await AddProductAsync(number, status);
            visibleIds.Add(ProductId(number));
        }

        await AddProductAsync(11, ProductStatus.Draft);
        await AddProductAsync(12, ProductStatus.Hidden);
        await AddProductAsync(13, ProductStatus.Draft);

        var seenIds = new List<string>();
        var pageCount = 0;
        string? cursor = null;
        do
        {
            var current = cursor;
            var page = await WithReaderAsync(reader => reader.ListForIndexingAsync(current, 2, CancellationToken.None));
            Assert.True(page.Items.Count <= 2);
            seenIds.AddRange(page.Items.Select(item => item.Id));
            pageCount++;
            cursor = page.Next;
            // Dừng cứng để một lỗi cursor lặp mãi không treo test.
            Assert.True(pageCount < 1000, "Cursor không bao giờ kết thúc.");
        }
        while (cursor is not null);

        Assert.True(pageCount >= 3, $"Chỉ duyệt {pageCount} trang.");
        Assert.Equal(seenIds.Count, seenIds.Distinct().Count());
        var mine = seenIds.Where(id => id.EndsWith(_suffix, StringComparison.Ordinal) && id.StartsWith("rd", StringComparison.Ordinal)).ToList();
        Assert.Equal(visibleIds.Order(StringComparer.Ordinal), mine);
    }

    [Fact]
    public async Task ListForIndexing_ReturnsFullDetailWithSpecsAndParentCategory()
    {
        var seeded = await AddProductAsync(1, specs: new Dictionary<string, object> { ["ram_gb"] = "8" });

        // Trang lớn nhất cho phép, để sản phẩm vừa tạo nằm trong trang đầu dù DB có dữ liệu khác.
        var page = await WithReaderAsync(reader => reader.ListForIndexingAsync(null, 500, CancellationToken.None));

        var item = page.Items.Single(candidate => candidate.Id == seeded.Id);
        Assert.Equal("8", item.Specs.Single(spec => spec.Key == "ram_gb").Value!.Value.GetString());
        Assert.Equal(_parentCategory.Id, item.ParentCategory!.Id);
    }

    [Fact]
    public async Task ListForIndexing_WithCursorAtLastId_ReturnsEmptyLastPage()
    {
        await AddProductAsync(1);

        // Cursor `zzzzzzzz` đứng sau mọi `Id` hex và `Id` của test theo cách DB sắp chữ, nên không còn sản phẩm nào phía sau.
        var page = await WithReaderAsync(reader => reader.ListForIndexingAsync("zzzzzzzz", 5, CancellationToken.None));

        Assert.Empty(page.Items);
        Assert.Null(page.Next);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(501)]
    public async Task ListForIndexing_WithPageSizeOutOfRange_Throws(int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => WithReaderAsync(reader => reader.ListForIndexingAsync(null, pageSize, CancellationToken.None)));
    }

    [Fact]
    public async Task GetAttributeDefinitions_ReturnsParentAttributesBeforeChildAttributes()
    {
        var childDefinitions = await WithReaderAsync(reader => reader.GetAttributeDefinitionsAsync(_childCategory.Id, CancellationToken.None));
        var parentDefinitions = await WithReaderAsync(reader => reader.GetAttributeDefinitionsAsync(_parentCategory.Id, CancellationToken.None));

        Assert.Equal(["ram_gb", "weight_kg", "gpu"], childDefinitions.Select(definition => definition.Key));
        Assert.Equal(["ram_gb", "weight_kg"], parentDefinitions.Select(definition => definition.Key));
        var ram = childDefinitions[0];
        Assert.Equal("Cấu hình", ram.Group);
        Assert.Equal(AttributeTypes.Enum, ram.Type);
        Assert.Equal("GB", ram.Unit);
        Assert.Equal(["8", "16"], ram.Options);
        Assert.True(ram.Filterable);
        Assert.False(childDefinitions[1].Filterable);
    }

    [Fact]
    public async Task GetAttributeDefinitions_OfUnknownCategory_ReturnsEmptyList()
    {
        var definitions = await WithReaderAsync(reader => reader.GetAttributeDefinitionsAsync("khong-ton-tai", CancellationToken.None));

        Assert.Empty(definitions);
    }

    [Fact]
    public async Task ListProducts_UsesTheSameFiltersAsThePublicRoute()
    {
        await AddProductAsync(1, skus: [new(20_000_000, 3)], specs: new Dictionary<string, object> { ["ram_gb"] = "16" });
        await AddProductAsync(2, skus: [new(30_000_000, 0)], specs: new Dictionary<string, object> { ["ram_gb"] = "16" });
        await AddProductAsync(3, skus: [new(25_000_000, 3)], specs: new Dictionary<string, object> { ["ram_gb"] = "8" });
        await AddProductAsync(4, ProductStatus.Draft, skus: [new(21_000_000, 3)], specs: new Dictionary<string, object> { ["ram_gb"] = "16" });

        var filters = new ProductFilters(
            CategorySlug: _parentCategory.Slug, InStockOnly: true,
            Attributes: new Dictionary<string, string> { ["ram_gb"] = "16" }, Sort: "price_asc");
        var products = await WithReaderAsync(reader => reader.ListProductsAsync(filters, 10, CancellationToken.None));

        var response = await _client.GetAsync($"/api/products?category={_parentCategory.Slug}&inStock=true&attr.ram_gb=16&sort=price_asc&pageSize=10");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var routeIds = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToList();

        Assert.Equal([ProductId(1)], products.Select(product => product.Id));
        Assert.Equal(routeIds, products.Select(product => product.Id));
    }

    [Fact]
    public async Task ListProducts_FiltersByBrandAndPriceRange_LikeThePublicRoute()
    {
        var otherBrand = await _data.AddExtraBrandAsync("khac");
        var mainBrandSlug = $"hang-doc-{_suffix}";
        await AddProductAsync(1, skus: [new(10_000_000, 3)]);
        await AddProductAsync(2, skus: [new(20_000_000, 3)]);
        await AddProductAsync(3, skus: [new(30_000_000, 3)]);
        await _data.AddProductAsync(new ReadingTestData.ProductSeed(
            ProductId(4), _childCategory.Id, Skus: [new(20_000_000, 3)], BrandId: otherBrand.Id));
        await _data.AddProductAsync(new ReadingTestData.ProductSeed(
            ProductId(5), _childCategory.Id, Skus: [new(40_000_000, 3)], BrandId: otherBrand.Id));

        // Mỗi trường hợp kiểm một bộ lọc. Tập id mong đợi viết cụ thể để reader và route cùng sai vẫn bị bắt.
        var cases = new (string Name, ProductFilters Filters, string Query, int[] Expected)[]
        {
            ("brand chính", new(CategorySlug: _parentCategory.Slug, BrandSlugs: [mainBrandSlug], Sort: "price_asc"),
                $"brand={mainBrandSlug}", [1, 2, 3]),
            ("brand khác", new(CategorySlug: _parentCategory.Slug, BrandSlugs: [otherBrand.Slug], Sort: "price_asc"),
                $"brand={otherBrand.Slug}", [4, 5]),
            ("hai brand", new(CategorySlug: _parentCategory.Slug, BrandSlugs: [mainBrandSlug, otherBrand.Slug], Sort: "price_asc"),
                $"brand={mainBrandSlug},{otherBrand.Slug}", [1, 2, 4, 3, 5]),
            ("priceMin", new(CategorySlug: _parentCategory.Slug, PriceMin: 20_000_000, Sort: "price_asc"),
                "priceMin=20000000", [2, 4, 3, 5]),
            ("priceMax", new(CategorySlug: _parentCategory.Slug, PriceMax: 20_000_000, Sort: "price_asc"),
                "priceMax=20000000", [1, 2, 4]),
            ("khoảng giá và brand", new(CategorySlug: _parentCategory.Slug, BrandSlugs: [otherBrand.Slug], PriceMin: 15_000_000, PriceMax: 30_000_000, Sort: "price_asc"),
                $"brand={otherBrand.Slug}&priceMin=15000000&priceMax=30000000", [4]),
        };

        foreach (var (name, filters, query, expected) in cases)
        {
            var products = await WithReaderAsync(reader => reader.ListProductsAsync(filters, 10, CancellationToken.None));

            var response = await _client.GetAsync($"/api/products?category={_parentCategory.Slug}&{query}&sort=price_asc&pageSize=10");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var routeIds = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()!).ToList();

            Assert.True(
                expected.Select(ProductId).SequenceEqual(products.Select(product => product.Id)),
                $"{name}: reader trả [{string.Join(',', products.Select(product => product.Id))}]");
            Assert.Equal(routeIds, products.Select(product => product.Id));
        }
    }

    [Fact]
    public async Task ListProducts_ClampsLimitToValidRange()
    {
        for (var number = 1; number <= 3; number++)
        {
            await AddProductAsync(number);
        }

        var filters = new ProductFilters(CategorySlug: _parentCategory.Slug, Sort: "price_asc");
        var belowMinimum = await WithReaderAsync(reader => reader.ListProductsAsync(filters, 0, CancellationToken.None));
        var negative = await WithReaderAsync(reader => reader.ListProductsAsync(filters, -5, CancellationToken.None));
        var two = await WithReaderAsync(reader => reader.ListProductsAsync(filters, 2, CancellationToken.None));
        var huge = await WithReaderAsync(reader => reader.ListProductsAsync(filters, 100_000, CancellationToken.None));

        Assert.Single(belowMinimum);
        Assert.Single(negative);
        Assert.Equal(2, two.Count);
        Assert.Equal(3, huge.Count);
    }

    [Fact]
    public async Task CountLowStock_CountsEnabledSkusAtOrBelowThreshold()
    {
        var before = await WithReaderAsync(reader => reader.CountLowStockAsync(CancellationToken.None));

        // Đúng ngưỡng (5) và hết hàng (0) tính, 6 không tính, SKU tắt không tính dù hết hàng.
        await _stockData.CreateProductAsync(
            new StockTestData.SkuSeed(5), new StockTestData.SkuSeed(6), new StockTestData.SkuSeed(0, Enabled: false),
            new StockTestData.SkuSeed(0), new StockTestData.SkuSeed(2));

        var after = await WithReaderAsync(reader => reader.CountLowStockAsync(CancellationToken.None));

        Assert.Equal(before + 3, after);
    }
}
