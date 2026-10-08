using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Descriptions;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;
using Pickory.Modules.Catalog.Seeding;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-12: `catalog:seed` ghi dữ liệu mẫu qua đường ghi của trang quản trị, và chạy lại không tạo bản ghi trùng.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CatalogSeedTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly SeedCatalog _catalog = CatalogSeedData.Create();

    public CatalogSeedTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
    }

    public Task InitializeAsync() => CleanSeedDataAsync();

    public async Task DisposeAsync()
    {
        // Kiểm bất biến trước khi dọn, vì dọn dữ liệu xóa luôn movement.
        await StockInvariant.AssertHoldsAsync(_database);
        await CleanSeedDataAsync();
    }

    // Xóa đúng dữ liệu mẫu theo slug, để test khác trong cùng DB không thấy thêm sản phẩm.
    private async Task CleanSeedDataAsync()
    {
        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var categorySlugs = _catalog.Categories.Select(category => SlugGenerator.ToSlug(category.Name)).ToList();
        var brandSlugs = _catalog.BrandNames.Select(SlugGenerator.ToSlug).ToList();
        var skuIds = context.Skus.Where(sku => productSlugs.Contains(sku.Product!.Slug)).Select(sku => sku.Id);
        // Movement giữ khóa ngoại Restrict tới SKU, nên phải xóa trước sản phẩm.
        await context.InventoryMovements.Where(movement => skuIds.Contains(movement.SkuId)).ExecuteDeleteAsync();
        await context.Products.Where(product => productSlugs.Contains(product.Slug)).ExecuteDeleteAsync();
        await context.Categories.Where(category => categorySlugs.Contains(category.Slug)).ExecuteDeleteAsync();
        await context.Brands.Where(brand => brandSlugs.Contains(brand.Slug)).ExecuteDeleteAsync();
    }

    // Mô phỏng ảnh đã có trên Cloudinary, vì kho ảnh giả của test từ chối publicId lạ.
    private void UploadSeedImages(SeedCatalog catalog)
    {
        foreach (var product in catalog.Products)
        {
            for (var number = 1; number <= product.ImageCount; number++)
            {
                _factory.ImageStorage.Add($"{CatalogSeeder.ImageFolder}/{product.Slug}-{number}", 200_000, "jpg");
            }
        }
    }

    private async Task<SeedReport> SeedAsync(SeedCatalog? catalog = null)
    {
        catalog ??= _catalog;
        UploadSeedImages(catalog);
        return await CatalogSeeder.SeedAsync(_factory.Services, catalog, CancellationToken.None);
    }

    private async Task<Dictionary<string, int>> CountRowsAsync()
    {
        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var skuIds = context.Skus.Where(sku => productSlugs.Contains(sku.Product!.Slug)).Select(sku => sku.Id);
        return new Dictionary<string, int>
        {
            ["categories"] = await context.Categories.CountAsync(category =>
                _catalog.Categories.Select(item => SlugGenerator.ToSlug(item.Name)).Contains(category.Slug)),
            ["brands"] = await context.Brands.CountAsync(brand =>
                _catalog.BrandNames.Select(SlugGenerator.ToSlug).Contains(brand.Slug)),
            ["products"] = await context.Products.CountAsync(product => productSlugs.Contains(product.Slug)),
            ["skus"] = await context.Skus.CountAsync(sku => skuIds.Contains(sku.Id)),
            ["images"] = await context.ProductImages.CountAsync(image => productSlugs.Contains(image.Product!.Slug)),
            ["movements"] = await context.InventoryMovements.CountAsync(movement => skuIds.Contains(movement.SkuId)),
        };
    }

    [Fact]
    public async Task Seed_RunTwice_DoesNotCreateDuplicates()
    {
        var first = await SeedAsync();
        var afterFirst = await CountRowsAsync();
        var second = await SeedAsync();
        var afterSecond = await CountRowsAsync();

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(5, afterFirst["categories"]);
        Assert.Equal(_catalog.Products.Count, afterFirst["products"]);
        Assert.Equal(_catalog.Products.Sum(product => product.Variants.Count), afterFirst["skus"]);
        Assert.Equal(_catalog.Products.Sum(product => product.ImageCount), afterFirst["images"]);
        Assert.Equal(_catalog.Products.Sum(product => product.Variants.Count(variant => variant.Stock > 0)), afterFirst["movements"]);
        // Lần chạy thứ hai không tạo gì mới ở bất kỳ loại bản ghi nào.
        Assert.Equal(_catalog.Products.Count, first.CreatedCount("Sản phẩm"));
        Assert.Equal(0, second.CreatedCount("Sản phẩm"));
        Assert.Equal(0, second.CreatedCount("Danh mục"));
        Assert.Equal(0, second.CreatedCount("Thương hiệu"));
        Assert.Equal(0, second.CreatedCount("Movement tồn kho"));
        Assert.Equal(0, second.CreatedCount("Chuyển trạng thái"));
        Assert.Equal(_catalog.Products.Count, second.SkippedCount("Sản phẩm"));
    }

    [Fact]
    public async Task Seed_EveryActiveProduct_PassesCheckPublishable()
    {
        await SeedAsync();

        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var products = await context.Products.AsNoTracking()
            .Include(product => product.Skus).Include(product => product.Images).Include(product => product.Category)
            .Where(product => productSlugs.Contains(product.Slug)).ToListAsync();

        var active = products.Where(product => product.Status == ProductStatus.Active).ToList();
        Assert.NotEmpty(active);
        foreach (var product in active)
        {
            var attributes = product.Category!.Attributes.Deserialize<List<AttributeDefinition>>(AttributeDefinition.JsonOptions)!;
            var input = new ProductPublishableInput(
                JsonNode.Parse(product.Specs.RootElement.GetRawText()), attributes, product.Images.Count,
                product.Skus.Select(sku => new SkuPublishableInput(sku.Code, sku.Enabled, sku.Price, sku.CompareAtPrice,
                    sku.WeightGrams, sku.LengthCm, sku.WidthCm, sku.HeightCm)).ToList());
            var check = ProductPublishing.CheckPublishable(input);
            Assert.True(check.Publishable, $"{product.Slug}: {string.Join(" | ", check.Reasons)}");
        }
    }

    [Fact]
    public async Task Seed_StatusesFollowTheSeedData()
    {
        await SeedAsync();

        await using var context = _database.CreateCatalogContext();
        var statusBySlug = await context.Products.AsNoTracking()
            .Where(product => _catalog.Products.Select(item => item.Slug).Contains(product.Slug))
            .ToDictionaryAsync(product => product.Slug, product => product.Status);
        foreach (var product in _catalog.Products)
        {
            Assert.Equal(product.Status, statusBySlug[product.Slug]);
        }

        Assert.True(_catalog.Products.Count(product => product.Status == ProductStatus.Discontinued) >= 2);
    }

    [Fact]
    public async Task Seed_StockOnlyChangesThroughOneManualMovementPerSku()
    {
        await SeedAsync();
        await SeedAsync();

        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var skus = await context.Skus.AsNoTracking().Include(sku => sku.Movements)
            .Where(sku => productSlugs.Contains(sku.Product!.Slug)).ToListAsync();

        var withStock = skus.Where(sku => sku.Stock > 0).ToList();
        Assert.NotEmpty(withStock);
        foreach (var sku in withStock)
        {
            var movement = Assert.Single(sku.Movements);
            Assert.Equal(InventoryMovementType.Manual, movement.Type);
            Assert.Equal("Dữ liệu mẫu", movement.Reason);
            Assert.Equal(sku.Stock, movement.Delta);
        }

        // SKU hết hàng không có movement nào, và tồn kho luôn bằng tổng movement.
        foreach (var sku in skus.Where(sku => sku.Stock == 0))
        {
            Assert.Empty(sku.Movements);
        }

        Assert.All(skus, sku => Assert.Equal(sku.Movements.Sum(movement => movement.Delta), sku.Stock));
        Assert.Contains(_catalog.Products, product => product.Variants.Any(variant => variant.Stock == 0));
    }

    [Fact]
    public async Task Seed_EveryDescription_PassesTheDescriptionSchema()
    {
        await SeedAsync();

        await using var context = _database.CreateCatalogContext();
        var productSlugs = _catalog.Products.Select(product => product.Slug).ToList();
        var descriptions = await context.Products.AsNoTracking()
            .Where(product => productSlugs.Contains(product.Slug)).Select(product => product.Description).ToListAsync();

        Assert.Equal(_catalog.Products.Count, descriptions.Count);
        foreach (var description in descriptions)
        {
            var validation = DescriptionSchema.Validate(JsonNode.Parse(description.RootElement.GetRawText()));
            Assert.True(validation.Valid, string.Join(" ", validation.Errors));
        }
    }

    [Fact]
    public async Task Seed_ProductsAppearInThePublicList_WithFacetCounts()
    {
        await SeedAsync();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/products?category=laptop&pageSize=48");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Laptop mẫu có 6 sản phẩm khách thấy được (5 đang bán và 1 ngừng kinh doanh).
        Assert.Equal(6, body.GetProperty("total").GetInt32());
        var ramFacet = body.GetProperty("facets").GetProperty("attributes").EnumerateArray()
            .Single(attribute => attribute.GetProperty("key").GetString() == "ram_gb");
        Assert.True(ramFacet.GetProperty("values").EnumerateArray().Sum(value => value.GetProperty("count").GetInt32()) > 0);

        var detail = await client.GetAsync($"/api/products/{_catalog.Products[0].Slug}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task Seed_DescriptionThatBreaksTheSchema_FailsWithTheProductName()
    {
        // Mô tả dài quá giới hạn ký tự của schema, nên lệnh dừng và nêu tên sản phẩm thay vì lưu tài liệu sai.
        var broken = _catalog.Products[0] with { Description = new string('a', DescriptionSchema.MaxTextLength + 1) };
        var catalog = new SeedCatalog(_catalog.Categories, [broken]);

        var error = await Assert.ThrowsAsync<SeedException>(() => SeedAsync(catalog));

        Assert.Contains(broken.Name, error.Message);
        Assert.Contains("không hợp lệ", error.Message);
    }

    [Fact]
    public async Task Seed_ProductThatCannotBePublished_FailsWithTheReasons()
    {
        // Sản phẩm không có ảnh nào thì không đủ điều kiện đăng bán, nên lệnh dừng và nêu lý do.
        var broken = _catalog.Products[0] with { ImageCount = 0 };
        var catalog = new SeedCatalog(_catalog.Categories, [broken]);

        var error = await Assert.ThrowsAsync<SeedException>(() => SeedAsync(catalog));

        Assert.Contains("ít nhất 1 ảnh", error.Message);
        Assert.Contains(broken.Name, error.Message);
    }
}
