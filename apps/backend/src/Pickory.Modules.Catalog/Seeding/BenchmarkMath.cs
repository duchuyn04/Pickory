using Npgsql;
using Pickory.Shared.Persistence;

namespace Pickory.Modules.Catalog.Seeding;

// Kết quả đo độ trễ của một lượt benchmark, tính bằng mili giây.
public sealed record LatencySummary(int Count, double P50, double P95, double Max)
{
    // Ngân sách tính bằng "dưới": p95 đúng bằng ngưỡng vẫn là không đạt.
    public bool IsP95Below(double limitMilliseconds) => P95 < limitMilliseconds;
}

public static class LatencyStatistics
{
    // Phương pháp hạng gần nhất: lấy giá trị ở hạng `ceil(p * n / 100)` của danh sách đã sắp xếp. Kết quả luôn là số đo thật.
    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("Cần ít nhất một số đo để tính phân vị.", nameof(values));
        }

        if (percentile is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), "Phân vị phải nằm trong khoảng (0, 100].");
        }

        var sorted = values.OrderBy(value => value).ToList();
        // Nhân trước khi chia để tránh sai số dấu phẩy động, ví dụ `0.95 * 100` không chắc ra đúng 95.
        var rank = (int)Math.Ceiling(percentile * sorted.Count / 100);
        return sorted[Math.Max(rank, 1) - 1];
    }

    public static LatencySummary Summarize(IReadOnlyList<double> values) =>
        new(values.Count, Percentile(values, 50), Percentile(values, 95), values.Max());
}

// Lệnh benchmark sinh 500 sản phẩm, nên chỉ được chạy trên DB có tên dành riêng cho việc đó.
public static class BenchmarkDatabase
{
    public const string RequiredNamePart = "bench";

    // Kiểm tên DB trước mọi thao tác ghi. Nhầm sang DB dev hoặc DB test sẽ làm bẩn dữ liệu của chúng.
    public static string EnsureBenchmarkDatabase(string databaseUrl)
    {
        var name = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Convert(databaseUrl)).Database ?? string.Empty;
        if (!name.Contains(RequiredNamePart, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"catalog:benchmark chỉ chạy trên DB có tên chứa \"{RequiredNamePart}\", ví dụ pickory_bench. DB hiện tại là \"{name}\".");
        }

        return name;
    }
}

public static class BenchmarkQueries
{
    private const string Route = "/api/products";

    // Trộn các kiểu truy vấn khách hay gọi: không lọc, danh mục, hãng, khoảng giá, thuộc tính, sắp xếp và trang 2.
    // Tham số thuộc tính áp lên danh mục đầu tiên, vì thuộc tính chỉ có nghĩa trong danh mục của nó.
    public static IReadOnlyList<string> BuildPaths(
        IReadOnlyList<string> categorySlugs, IReadOnlyList<string> brandSlugs, string attributeParameter)
    {
        var paths = new List<string> { Route };
        foreach (var category in categorySlugs)
        {
            paths.Add($"{Route}?category={category}");
            paths.Add($"{Route}?category={category}&sort=price_asc");
            paths.Add($"{Route}?category={category}&page=2");
        }

        foreach (var brand in brandSlugs)
        {
            paths.Add($"{Route}?brand={brand}");
        }

        paths.Add($"{Route}?priceMin=5000000&priceMax=30000000");
        paths.Add($"{Route}?inStock=true&sort=price_desc");
        paths.Add($"{Route}?sort=price_desc");
        paths.Add($"{Route}?page=2");
        if (categorySlugs.Count > 0)
        {
            paths.Add($"{Route}?category={categorySlugs[0]}&{attributeParameter}");
        }

        return paths.Distinct().ToList();
    }

    // Lặp vòng danh sách tới đủ số request, để mọi kiểu truy vấn đều được đo nhiều lần.
    public static IEnumerable<string> Repeat(IReadOnlyList<string> paths, int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return paths[index % paths.Count];
        }
    }
}
