using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;

namespace Pickory.IntegrationTests.Infrastructure;

// Dựng sản phẩm và SKU có tồn kho cho test CAT-08. Tồn kho đầu kỳ luôn đi kèm một movement `manual` khớp số tồn.
public sealed class StockTestData(IntegrationDatabaseFixture database, string suffix)
{
    public sealed record SkuSeed(int Stock, int Price = 1_000_000, bool Enabled = true);

    public sealed record SeededProduct(string ProductId, IReadOnlyList<string> SkuIds);

    private readonly List<string> _productIds = new();
    private string _categoryId = null!;
    private string _brandId = null!;

    public string CategoryId => _categoryId;

    public string BrandId => _brandId;

    // Sản phẩm tạo bằng API cũng cần được dọn sau test.
    public void TrackProduct(string productId) => _productIds.Add(productId);

    public async Task InitializeAsync()
    {
        await using var context = database.CreateCatalogContext();
        var category = new Category
        {
            Id = Guid.NewGuid().ToString("N"), Name = $"Tồn kho {suffix}", Slug = $"ton-kho-{suffix}",
            Position = 0, Attributes = JsonDocument.Parse("[]"), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var brand = new Brand
        {
            Id = Guid.NewGuid().ToString("N"), Name = $"Hãng tồn kho {suffix}", Slug = $"hang-ton-kho-{suffix}",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        context.Categories.Add(category);
        context.Brands.Add(brand);
        await context.SaveChangesAsync();
        _categoryId = category.Id;
        _brandId = brand.Id;
    }

    public async Task<SeededProduct> CreateProductAsync(params SkuSeed[] skus)
    {
        var productId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var display = ProductPricing.ComputeDisplayPrice(skus.Select(sku => new SkuPricingInput(sku.Price, sku.Stock, sku.Enabled)).ToList());
        var product = new Product
        {
            Id = productId, CategoryId = _categoryId, BrandId = _brandId,
            Name = $"Sản phẩm tồn kho {productId}", Slug = $"ton-kho-{productId}", ShortDesc = "Mô tả ngắn",
            Description = JsonDocument.Parse("""{"type":"doc","content":[{"type":"paragraph"}]}"""),
            Specs = JsonDocument.Parse("{}"), Options = JsonDocument.Parse("[]"), WarrantyMonths = 12,
            Status = ProductStatus.Active, MinPrice = display.MinPrice, InStock = display.InStock,
            CreatedAt = now, UpdatedAt = now,
        };

        var skuIds = new List<string>();
        for (var index = 0; index < skus.Length; index++)
        {
            var seed = skus[index];
            var sku = new Sku
            {
                Id = Guid.NewGuid().ToString("N"), ProductId = productId, Code = $"TK-{suffix}-{productId[..6]}-{index}",
                OptionValues = JsonDocument.Parse($$"""{"i":"{{index}}"}"""), Price = seed.Price, Enabled = seed.Enabled,
                Stock = seed.Stock, WeightGrams = 200, LengthCm = 15, WidthCm = 10, HeightCm = 5,
                CreatedAt = now, UpdatedAt = now,
            };
            product.Skus.Add(sku);
            skuIds.Add(sku.Id);
            if (seed.Stock > 0)
            {
                // Movement đầu kỳ giữ bất biến `stock = tổng delta` ngay từ lúc seed.
                sku.Movements.Add(new InventoryMovement
                {
                    Id = Guid.NewGuid().ToString("N"), SkuId = sku.Id, Delta = seed.Stock,
                    Type = InventoryMovementType.Manual, Reason = "Tồn kho đầu kỳ của test", CreatedAt = now,
                });
            }
        }

        await using var context = database.CreateCatalogContext();
        context.Products.Add(product);
        await context.SaveChangesAsync();
        _productIds.Add(productId);
        return new SeededProduct(productId, skuIds);
    }

    public async Task CleanupAsync()
    {
        await using var context = database.CreateCatalogContext();
        // Movement giữ khóa ngoại Restrict tới SKU, nên phải xóa trước sản phẩm.
        await context.InventoryMovements
            .Where(movement => context.Skus.Any(sku => sku.Id == movement.SkuId && _productIds.Contains(sku.ProductId)))
            .ExecuteDeleteAsync();
        await context.Products.Where(product => _productIds.Contains(product.Id)).ExecuteDeleteAsync();
        await context.Categories.Where(category => category.Id == _categoryId).ExecuteDeleteAsync();
        await context.Brands.Where(brand => brand.Id == _brandId).ExecuteDeleteAsync();
    }

    public async Task<int> StockOfAsync(string skuId)
    {
        await using var context = database.CreateCatalogContext();
        return await context.Skus.AsNoTracking().Where(sku => sku.Id == skuId).Select(sku => sku.Stock).SingleAsync();
    }

    public async Task<(int? MinPrice, bool InStock)> DisplayOfAsync(string productId)
    {
        await using var context = database.CreateCatalogContext();
        var product = await context.Products.AsNoTracking().SingleAsync(item => item.Id == productId);
        return (product.MinPrice, product.InStock);
    }

    public async Task<List<InventoryMovement>> MovementsOfAsync(string skuId)
    {
        await using var context = database.CreateCatalogContext();
        return await context.InventoryMovements.AsNoTracking().Where(movement => movement.SkuId == skuId)
            .OrderBy(movement => movement.CreatedAt).ThenBy(movement => movement.Id).ToListAsync();
    }
}
