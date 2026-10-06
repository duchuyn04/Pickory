using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Database.Catalog;

// Port từ `schema-constraints.int.test.ts` của bản Node (CAT-03). DB tự chặn dữ liệu sai, kể cả khi code có lỗi.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CatalogConstraintTests(IntegrationDatabaseFixture database)
{
    // Tạo danh mục, thương hiệu, sản phẩm và một SKU với slug riêng cho mỗi test.
    private static async Task<Sku> CreateSkuAsync(CatalogDbContext catalog, int stock)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Name = "Laptop", Slug = $"laptop-{suffix}", Attributes = JsonDocument.Parse("[]") };
        var brand = new Brand { Name = "ASUS", Slug = $"asus-{suffix}" };
        var product = new Product
        {
            Category = category,
            Brand = brand,
            Name = "ASUS ROG Strix",
            Slug = $"asus-rog-strix-{suffix}",
            ShortDesc = "Laptop gaming",
            Description = JsonDocument.Parse("""{"type":"doc","content":[{"type":"paragraph"}]}"""),
            Specs = JsonDocument.Parse("{}"),
            Options = JsonDocument.Parse("[]"),
        };
        var sku = new Sku { Product = product, Code = $"SKU-{suffix}", OptionValues = JsonDocument.Parse("{}"), Price = 20_000_000, Stock = stock };
        catalog.Skus.Add(sku);
        await catalog.SaveChangesAsync();
        return sku;
    }

    [Fact]
    // `CHECK (stock >= 0)` từ chối tồn kho âm.
    public async Task Stock_CannotBeNegative()
    {
        await using var catalog = database.CreateCatalogContext();
        var sku = await CreateSkuAsync(catalog, stock: 5);

        sku.Stock = -1;

        await Assert.ThrowsAsync<DbUpdateException>(() => catalog.SaveChangesAsync());
    }

    [Fact]
    // Hai movement cùng `orderId`, `skuId` và `type` bị từ chối. Đơn hàng không giữ hàng hai lần.
    public async Task InventoryMovement_IsUniquePerOrderSkuAndType()
    {
        await using var catalog = database.CreateCatalogContext();
        var sku = await CreateSkuAsync(catalog, stock: 10);
        var orderId = $"order-{Guid.NewGuid():N}";
        catalog.InventoryMovements.Add(new InventoryMovement { SkuId = sku.Id, Delta = -1, Type = InventoryMovementType.OrderReserve, OrderId = orderId, Reason = "Giữ hàng cho đơn hàng" });
        await catalog.SaveChangesAsync();

        catalog.InventoryMovements.Add(new InventoryMovement { SkuId = sku.Id, Delta = -1, Type = InventoryMovementType.OrderReserve, OrderId = orderId, Reason = "Giữ hàng cho đơn hàng" });

        await Assert.ThrowsAsync<DbUpdateException>(() => catalog.SaveChangesAsync());
    }

    [Fact]
    // Cột `specs` có chỉ mục GIN, để lọc theo thông số không phải quét cả bảng.
    public async Task Products_HaveSpecsGinIndex()
    {
        await using var catalog = database.CreateCatalogContext();

        var indexDefinitions = await catalog.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'products' AND indexname = 'products_specs_gin_idx'")
            .ToListAsync();

        Assert.Contains("USING gin", Assert.Single(indexDefinitions));
    }

    [Fact]
    // Chỉ mục cho sắp xếp theo giá, theo ngày tạo và cho tra SKU theo sản phẩm đều tồn tại.
    public async Task ProductsAndSkus_HaveSupportingIndexes()
    {
        await using var catalog = database.CreateCatalogContext();

        var indexNames = await catalog.Database
            .SqlQuery<string>($"SELECT indexname AS \"Value\" FROM pg_indexes WHERE indexname IN ('products_minPrice_idx', 'products_createdAt_idx', 'skus_productId_idx')")
            .ToListAsync();

        Assert.Equal(["products_createdAt_idx", "products_minPrice_idx", "skus_productId_idx"], indexNames.Order());
    }

    [Fact]
    // Sản phẩm có SKU đã có movement thì không xóa được. Lịch sử tồn kho phải được giữ.
    public async Task Product_CannotBeDeletedWhileSkuHasMovements()
    {
        await using var catalog = database.CreateCatalogContext();
        var sku = await CreateSkuAsync(catalog, stock: 5);
        catalog.InventoryMovements.Add(new InventoryMovement { SkuId = sku.Id, Delta = 5, Type = InventoryMovementType.Manual, Reason = "Nhập kho ban đầu" });
        await catalog.SaveChangesAsync();

        var deleteProduct = () => catalog.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM products WHERE id = {sku.ProductId}");

        await Assert.ThrowsAnyAsync<Exception>(deleteProduct);
        await using var verify = database.CreateCatalogContext();
        Assert.True(await verify.Products.AnyAsync(product => product.Id == sku.ProductId));
    }
}
