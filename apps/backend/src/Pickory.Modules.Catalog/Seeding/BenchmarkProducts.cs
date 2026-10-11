using Pickory.Modules.Catalog.Entities;

namespace Pickory.Modules.Catalog.Seeding;

// Sinh bộ sản phẩm đông cho lệnh benchmark. Công thức cố định, nên hai lần đo dùng cùng một dữ liệu.
public static class BenchmarkProducts
{
    private const int MinPrice = 500_000;
    private const int MaxPrice = 60_000_000;
    private const int FixedRandomSeed = 2026;
    private const string BenchmarkImageUrl = "https://example.com/benchmark.jpg";

    public static SeedCatalog Generate(SeedCatalog template, int count)
    {
        var random = new Random(FixedRandomSeed);
        var products = new List<SeedProduct>(count);
        for (var index = 0; index < count; index++)
        {
            // Chia vòng qua các danh mục để mỗi danh mục có số sản phẩm bằng nhau (cách nhau không quá một).
            var category = template.Categories[index % template.Categories.Count];
            // Thông số lấy vòng từ sản phẩm thật cùng danh mục, nên luôn hợp lệ với bộ thuộc tính của danh mục.
            var sources = template.Products.Where(product => product.CategoryName == category.Name).ToList();
            var source = sources[index / template.Categories.Count % sources.Count];
            var brandName = template.BrandNames[index % template.BrandNames.Count];
            var name = $"Sản phẩm benchmark {index + 1:0000} {category.Name}";
            // Giá làm tròn tới nghìn đồng cho giống giá bán thật.
            var price = random.Next(MinPrice / 1000, MaxPrice / 1000 + 1) * 1000;

            products.Add(new SeedProduct(
                name, category.Name, brandName,
                $"Sản phẩm sinh tự động để đo hiệu năng danh sách, thuộc danh mục {category.Name}.",
                $"Sản phẩm benchmark số {index + 1} thuộc danh mục {category.Name}.",
                source.Specs, [], [new SeedVariant(new Dictionary<string, string>(), price, Stock: 10)],
                ProductStatus.Active)
            {
                // Benchmark chạy với kho ảnh giả nên URL này không bao giờ được tải.
                Images = [new SeedImage(BenchmarkImageUrl)],
            });
        }

        return new SeedCatalog(template.Categories, products);
    }
}
