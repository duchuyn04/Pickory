using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Seeding;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Seeding;

// CAT-12: bộ 500 sản phẩm của lệnh benchmark sinh theo công thức cố định, nên mỗi lần đo cùng một dữ liệu.
public sealed class BenchmarkProductsTests
{
    private static readonly SeedCatalog Template = CatalogSeedData.Create();

    [Fact]
    public void Generate_ReturnsTheRequestedNumberOfActiveProducts()
    {
        var catalog = BenchmarkProducts.Generate(Template, 500);

        Assert.Equal(500, catalog.Products.Count);
        Assert.All(catalog.Products, product => Assert.Equal(ProductStatus.Active, product.Status));
    }

    [Fact]
    public void Generate_GivesEveryProductADistinctNameAndSlug()
    {
        var catalog = BenchmarkProducts.Generate(Template, 500);

        Assert.Equal(500, catalog.Products.Select(product => product.Name).Distinct().Count());
        Assert.Equal(500, catalog.Products.Select(product => product.Slug).Distinct().Count());
    }

    [Fact]
    public void Generate_SpreadsProductsEvenlyAcrossTheFiveCategories()
    {
        var catalog = BenchmarkProducts.Generate(Template, 500);

        var counts = catalog.Products.GroupBy(product => product.CategoryName).Select(group => group.Count()).ToList();
        Assert.Equal(5, counts.Count);
        Assert.All(counts, count => Assert.Equal(100, count));
    }

    [Fact]
    public void Generate_ReusesTheSpecKeysOfTheTemplateProductsOfTheSameCategory()
    {
        var catalog = BenchmarkProducts.Generate(Template, 50);

        foreach (var product in catalog.Products)
        {
            var allowedKeys = Template.Products
                .Where(item => item.CategoryName == product.CategoryName)
                .SelectMany(item => item.Specs.Keys)
                .ToHashSet();
            Assert.All(product.Specs.Keys, key => Assert.Contains(key, allowedKeys));
        }
    }

    [Fact]
    public void Generate_EveryProductHasOneInStockVariantWithAPositivePrice()
    {
        var catalog = BenchmarkProducts.Generate(Template, 100);

        foreach (var product in catalog.Products)
        {
            var variant = Assert.Single(product.Variants);
            Assert.True(variant.Price > 0);
            Assert.True(variant.Stock > 0);
            Assert.Empty(product.Options);
        }
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var first = BenchmarkProducts.Generate(Template, 100).Products.Select(product => (product.Name, product.Variants[0].Price));
        var second = BenchmarkProducts.Generate(Template, 100).Products.Select(product => (product.Name, product.Variants[0].Price));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Generate_KeepsTheTemplateCategories()
    {
        var catalog = BenchmarkProducts.Generate(Template, 10);

        Assert.Equal(Template.Categories, catalog.Categories);
    }
}
