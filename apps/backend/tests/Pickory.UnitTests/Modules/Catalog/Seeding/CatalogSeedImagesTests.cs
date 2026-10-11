using Pickory.Modules.Catalog.Images;
using Pickory.Modules.Catalog.Seeding;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Seeding;

// Bộ dữ liệu mẫu có 50 sản phẩm. Mỗi danh mục gốc đủ 10 sản phẩm để dải trang chủ đầy, và mỗi sản phẩm có gallery hợp lệ.
public sealed class CatalogSeedImagesTests
{
    private static readonly SeedCatalog Catalog = CatalogSeedData.Create();

    [Fact]
    public void Create_HasTenProductsInEveryRootCategory()
    {
        Assert.Equal(50, Catalog.Products.Count);
        foreach (var category in Catalog.Categories)
        {
            Assert.Equal(10, Catalog.Products.Count(product => product.CategoryName == category.Name));
        }
    }

    [Fact]
    public void Create_EveryProductHasThreeToSixImages()
    {
        Assert.All(Catalog.Products, product =>
            Assert.True(product.Images.Count is >= 3 and <= 6, $"{product.Name}: {product.Images.Count} ảnh"));
    }

    [Fact]
    public void Create_EverySourceUrlIsHttpsAndUniqueWithinTheProduct()
    {
        foreach (var product in Catalog.Products)
        {
            var urls = product.Images.Select(image => image.SourceUrl).ToList();
            Assert.All(urls, url =>
            {
                Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri), $"{product.Name}: {url}");
                Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            });
            Assert.Equal(urls.Count, urls.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Create_EveryProductKeepsTheImageCountWithinTheUploadLimit()
    {
        Assert.All(Catalog.Products, product => Assert.True(product.Images.Count <= ImageUploadRules.MaxImagesPerProduct));
    }

    [Fact]
    public void Create_GivesEveryProductADistinctSlug()
    {
        Assert.Equal(50, Catalog.Products.Select(product => product.Slug).Distinct().Count());
    }

    [Fact]
    public void ImagePublicId_StartsAtOneAndStaysInsideTheProductFolder()
    {
        var product = Catalog.Products[0];

        var first = CatalogSeeder.ImagePublicId(product, 1);

        Assert.Equal($"pickory/products/seed/{product.Slug}-1", first);
        Assert.True(ImageUploadRules.IsProductPublicId(first));
    }
}
