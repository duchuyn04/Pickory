using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;

namespace Pickory.IntegrationTests.Infrastructure;

// Dựng danh mục, hãng, sản phẩm, SKU và ảnh cho test CAT-10. Mỗi test dùng `suffix` riêng để không đụng dữ liệu của test khác.
public sealed class ReadingTestData(IntegrationDatabaseFixture database, string suffix)
{
    public sealed record SkuSeed(int Price, int Stock, bool Enabled = true, int? CompareAtPrice = null);

    // `SkuIndex` trỏ tới vị trí của SKU trong danh sách `Skus` của sản phẩm. `null` nghĩa là ảnh chung của sản phẩm.
    public sealed record ImageSeed(string PublicId, int Position, int? SkuIndex = null);

    public sealed record ProductSeed(
        string Id,
        string CategoryId,
        ProductStatus Status = ProductStatus.Active,
        string? Slug = null,
        IReadOnlyDictionary<string, object>? Specs = null,
        IReadOnlyList<SkuSeed>? Skus = null,
        IReadOnlyList<ImageSeed>? Images = null,
        string? BrandId = null,
        string? Name = null,
        string? ShortDesc = null,
        string? DescriptionJson = null);

    public sealed record SeededProduct(string Id, string Slug, IReadOnlyList<string> SkuIds);

    private readonly List<string> _productIds = new();
    private readonly List<string> _categoryIds = new();
    private readonly List<string> _brandIds = new();

    public string BrandId { get; private set; } = null!;

    public async Task AddBrandAsync()
    {
        await using var context = database.CreateCatalogContext();
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"), Name = $"Hãng đọc {suffix}", Slug = $"hang-doc-{suffix}",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        context.Brands.Add(brand);
        await context.SaveChangesAsync();
        _brandIds.Add(brand.Id);
        BrandId = brand.Id;
    }

    // Thêm hãng thứ hai để test lọc theo hãng. Trả về id và slug của hãng.
    public async Task<(string Id, string Slug)> AddExtraBrandAsync(string key)
    {
        await using var context = database.CreateCatalogContext();
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"), Name = $"Hãng {key} {suffix}", Slug = $"hang-{key}-{suffix}",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        context.Brands.Add(brand);
        await context.SaveChangesAsync();
        _brandIds.Add(brand.Id);
        return (brand.Id, brand.Slug);
    }

    public async Task<Category> AddCategoryAsync(string name, string? parentId, IReadOnlyList<AttributeDefinition> attributes)
    {
        await using var context = database.CreateCatalogContext();
        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"), ParentId = parentId, Name = $"{name} {suffix}",
            Slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{suffix}", Position = 0,
            Attributes = JsonDocument.Parse(JsonSerializer.Serialize(attributes, AttributeDefinition.JsonOptions)),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        _categoryIds.Add(category.Id);
        return category;
    }

    public async Task<SeededProduct> AddProductAsync(ProductSeed seed)
    {
        var now = DateTime.UtcNow;
        var skuSeeds = seed.Skus ?? [new SkuSeed(1_000_000, 10)];
        var display = ProductPricing.ComputeDisplayPrice(
            skuSeeds.Select(sku => new SkuPricingInput(sku.Price, sku.Stock, sku.Enabled)).ToList());
        var slug = seed.Slug ?? $"san-pham-{seed.Id}";
        var product = new Product
        {
            Id = seed.Id, CategoryId = seed.CategoryId, BrandId = seed.BrandId ?? BrandId,
            Name = seed.Name ?? $"Sản phẩm {seed.Id}", Slug = slug, ShortDesc = seed.ShortDesc ?? "Mô tả ngắn",
            Description = JsonDocument.Parse(seed.DescriptionJson ?? """{"type":"doc","content":[{"type":"paragraph"}]}"""),
            Specs = JsonDocument.Parse(JsonSerializer.Serialize(seed.Specs ?? new Dictionary<string, object>())),
            Options = JsonDocument.Parse("""[{"name":"Màu","values":["Đỏ","Xanh"]}]"""),
            WarrantyMonths = 12, Status = seed.Status, MinPrice = display.MinPrice, InStock = display.InStock,
            CreatedAt = now, UpdatedAt = now,
        };

        var skuIds = new List<string>();
        for (var index = 0; index < skuSeeds.Count; index++)
        {
            var skuSeed = skuSeeds[index];
            var sku = new Sku
            {
                Id = Guid.NewGuid().ToString("N"), ProductId = seed.Id, Code = $"DOC-{suffix}-{seed.Id}-{index}",
                OptionValues = JsonDocument.Parse($$"""{"Màu":"{{(index == 0 ? "Đỏ" : "Xanh")}}"}"""),
                Price = skuSeed.Price, CompareAtPrice = skuSeed.CompareAtPrice, Stock = skuSeed.Stock, Enabled = skuSeed.Enabled,
                WeightGrams = 300 + index, LengthCm = 20, WidthCm = 10, HeightCm = 2,
                // Lệch `CreatedAt` theo chỉ số để thứ tự SKU trong chi tiết xác định được.
                CreatedAt = now.AddSeconds(index), UpdatedAt = now,
            };
            if (skuSeed.Stock > 0)
            {
                // Movement đầu kỳ giữ bất biến `stock = tổng delta`, để test CAT-08 chạy sau không thấy dữ liệu sai.
                sku.Movements.Add(new InventoryMovement
                {
                    Id = Guid.NewGuid().ToString("N"), SkuId = sku.Id, Delta = skuSeed.Stock,
                    Type = InventoryMovementType.Manual, Reason = "Tồn kho đầu kỳ của test", CreatedAt = now,
                });
            }

            product.Skus.Add(sku);
            skuIds.Add(sku.Id);
        }

        foreach (var image in seed.Images ?? [])
        {
            product.Images.Add(new ProductImage
            {
                Id = Guid.NewGuid().ToString("N"), ProductId = seed.Id, PublicId = image.PublicId,
                Alt = $"Alt {image.PublicId}", Position = image.Position,
                SkuId = image.SkuIndex is null ? null : skuIds[image.SkuIndex.Value], CreatedAt = now,
            });
        }

        await using var context = database.CreateCatalogContext();
        context.Products.Add(product);
        await context.SaveChangesAsync();
        _productIds.Add(seed.Id);
        return new SeededProduct(seed.Id, slug, skuIds);
    }

    public async Task AddSlugHistoryAsync(string productId, string oldSlug)
    {
        await using var context = database.CreateCatalogContext();
        context.ProductSlugHistories.Add(new ProductSlugHistory
        {
            Id = Guid.NewGuid().ToString("N"), ProductId = productId, OldSlug = oldSlug, CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    public async Task CleanupAsync()
    {
        await using var context = database.CreateCatalogContext();
        // Movement giữ khóa ngoại Restrict tới SKU, nên phải xóa trước sản phẩm.
        await context.InventoryMovements
            .Where(movement => context.Skus.Any(sku => sku.Id == movement.SkuId && _productIds.Contains(sku.ProductId)))
            .ExecuteDeleteAsync();
        // Xóa sản phẩm trước vì khóa ngoại tới danh mục và hãng. SKU, ảnh và lịch sử slug xóa theo.
        await context.Products.Where(product => _productIds.Contains(product.Id)).ExecuteDeleteAsync();
        // Danh mục con sinh sau danh mục cha, nên xóa theo thứ tự ngược.
        foreach (var categoryId in Enumerable.Reverse(_categoryIds))
        {
            await context.Categories.Where(category => category.Id == categoryId).ExecuteDeleteAsync();
        }

        await context.Brands.Where(brand => _brandIds.Contains(brand.Id)).ExecuteDeleteAsync();
    }
}
