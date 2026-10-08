using System.Globalization;
using FluentValidation;
using FluentValidation.Results;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Tham số đã kiểm của `GET /api/products`. Tham số `attr.*` giữ dạng chữ vì chỉ biết kiểu sau khi tra danh mục.
public sealed record ProductSearchQuery(
    string? CategorySlug,
    IReadOnlyList<string> BrandSlugs,
    int? PriceMin,
    int? PriceMax,
    bool InStockOnly,
    IReadOnlyDictionary<string, string> AttributeParameters,
    ProductSort Sort,
    int Page,
    int PageSize)
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 48;

    public int Skip => (Page - 1) * PageSize;

    // Gom mọi lỗi của query string vào một `ValidationException` để người gọi sửa được một lần.
    public static ProductSearchQuery Read(IReadOnlyDictionary<string, string> parameters)
    {
        var failures = new List<ValidationFailure>();

        var categorySlug = ReadText(parameters, "category");
        var brandSlugs = parameters.TryGetValue("brand", out var rawBrands)
            ? AttributeFilterReader.SplitList(rawBrands)
            : [];
        var priceMin = ReadPrice(parameters, "priceMin", failures);
        var priceMax = ReadPrice(parameters, "priceMax", failures);
        var inStockOnly = ReadInStock(parameters, failures);
        var sort = ReadSort(parameters, failures);
        var page = ReadInteger(parameters, "page", defaultValue: 1, minimum: 1, maximum: int.MaxValue,
            "Số trang phải là số nguyên lớn hơn hoặc bằng 1.", failures);
        var pageSize = ReadInteger(parameters, "pageSize", DefaultPageSize, minimum: 1, MaxPageSize,
            $"Số sản phẩm mỗi trang phải là số nguyên từ 1 đến {MaxPageSize}.", failures);

        // Khoảng giá ngược cho tập rỗng, nên báo lỗi thay vì trả danh sách trống khó hiểu.
        if (priceMin > priceMax)
        {
            failures.Add(new ValidationFailure("priceMin", "Giá thấp nhất không được lớn hơn giá cao nhất."));
        }

        // `Skip` của EF Core là `int`. Độ lệch vượt `int.MaxValue` sẽ tràn thành số âm và DB trả lỗi 500.
        if ((long)(page - 1) * pageSize > int.MaxValue)
        {
            failures.Add(new ValidationFailure("page", "Số trang vượt quá giới hạn cho phép."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // Giữ nguyên các tham số `attr.*`. Bước sau mới đổi chúng thành bộ lọc theo thuộc tính của danh mục.
        var attributeParameters = parameters
            .Where(pair => pair.Key.StartsWith(AttributeFilterReader.ParameterPrefix, StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new ProductSearchQuery(
            categorySlug, brandSlugs, priceMin, priceMax, inStockOnly, attributeParameters, sort, page, pageSize);
    }

    private static string? ReadText(IReadOnlyDictionary<string, string> parameters, string name)
    {
        // Giá trị rỗng coi như không lọc, vì form tìm kiếm hay gửi ô trống.
        return parameters.TryGetValue(name, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue)
            ? rawValue.Trim()
            : null;
    }

    private static int? ReadPrice(IReadOnlyDictionary<string, string> parameters, string name, List<ValidationFailure> failures)
    {
        if (!parameters.TryGetValue(name, out var rawValue))
        {
            return null;
        }

        // `NumberStyles.None` chặn dấu âm và dấu thập phân. Giá là số đồng nguyên không âm.
        if (!int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var price))
        {
            failures.Add(new ValidationFailure(name, "Giá phải là số nguyên không âm."));
            return null;
        }

        return price;
    }

    private static bool ReadInStock(IReadOnlyDictionary<string, string> parameters, List<ValidationFailure> failures)
    {
        if (!parameters.TryGetValue("inStock", out var rawValue))
        {
            return false;
        }

        // Chỉ nhận đúng `true` hoặc `false` để giá trị lạ không âm thầm bỏ bộ lọc.
        if (rawValue is "true" or "false")
        {
            return rawValue == "true";
        }

        failures.Add(new ValidationFailure("inStock", "Giá trị phải là true hoặc false."));
        return false;
    }

    private static ProductSort ReadSort(IReadOnlyDictionary<string, string> parameters, List<ValidationFailure> failures)
    {
        if (!parameters.TryGetValue("sort", out var rawValue))
        {
            return ProductSort.Newest;
        }

        switch (rawValue)
        {
            case "newest":
                return ProductSort.Newest;
            case "price_asc":
                return ProductSort.PriceAscending;
            case "price_desc":
                return ProductSort.PriceDescending;
            default:
                failures.Add(new ValidationFailure("sort", "Kiểu sắp xếp phải là newest, price_asc hoặc price_desc."));
                return ProductSort.Newest;
        }
    }

    private static int ReadInteger(
        IReadOnlyDictionary<string, string> parameters, string name, int defaultValue, int minimum, int maximum,
        string message, List<ValidationFailure> failures)
    {
        if (!parameters.TryGetValue(name, out var rawValue))
        {
            return defaultValue;
        }

        // Ngoài khoảng cho phép thì từ chối. Trần `pageSize` giữ mỗi request không kéo về cả bảng.
        if (!int.TryParse(rawValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || value < minimum || value > maximum)
        {
            failures.Add(new ValidationFailure(name, message));
            return defaultValue;
        }

        return value;
    }
}
